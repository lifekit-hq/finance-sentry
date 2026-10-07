namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// The discovery pass against a real Postgres-backed <see cref="BankSyncDbContext"/> shared by the real
/// repositories and the real token-refresh service (as in the Scoped container): a refresh the provider
/// rejects with invalid_grant must expire the connection and flag its accounts reauth_required through
/// EF's change tracker, without aborting discovery for the connections behind it. Mocked repositories
/// cannot see the tracked/detached-entity conflict this guards.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AccountDiscoveryDeadConnectionTests : IAsyncLifetime
{
    private const byte DeadMarker = 1;
    private const byte HealthyMarker = 2;
    private const string HealthyAccessToken = "healthy-access";
    private readonly Guid _userId = Guid.NewGuid();
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();
        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private BankSyncDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<BankSyncDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private static TrueLayerConnection LinkedConnection(Guid userId, string suffix, byte marker)
    {
        var connection = new TrueLayerConnection(userId, $"ob-sandbox-{suffix}", $"Sandbox {suffix}", $"ref-{Guid.NewGuid():N}");
        connection.SetRefreshToken([marker], [4], [5], 1);
        connection.MarkLinked(DateTime.UtcNow.AddDays(30));
        return connection;
    }

    private static BankAccount AccountOn(Guid userId, TrueLayerConnection connection)
    {
        var account = new BankAccount(userId, $"ext-{Guid.NewGuid():N}", "Sandbox", "checking", "1234", "Owner",
            "EUR", userId, "truelayer")
        {
            TrueLayerConnectionId = connection.Id,
        };
        return account;
    }

    private (AccountDiscoveryService Sut, Mock<ITrueLayerClient> Client) BuildSut(BankSyncDbContext context)
    {
        var connections = new TrueLayerConnectionRepository(context);
        var accounts = new BankAccountRepository(context);

        var encryption = new Mock<ICredentialEncryptionService>();
        // The ciphertext's first byte marks which connection is being refreshed.
        encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .Returns((byte[] ct, byte[] _, byte[] _, int _) => ct[0] == DeadMarker ? "dead-token" : "healthy-token");

        var client = new Mock<ITrueLayerClient>();
        client.Setup(c => c.ListCardsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<IState>())).Returns("job-id");

        var refresh = new TrueLayerTokenRefreshService(connections, encryption.Object, client.Object);
        var monobankCredentials = new Mock<IMonobankCredentialRepository>();
        monobankCredentials.Setup(r => r.GetAllUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var sut = new AccountDiscoveryService(
            connections, client.Object, refresh, monobankCredentials.Object, new Mock<IMonobankAdapter>().Object,
            new MonobankBalanceCache(), encryption.Object, accounts, jobs.Object,
            NullLogger<AccountDiscoveryService>.Instance);
        return (sut, client);
    }

    private static void StubProvider(Mock<ITrueLayerClient> client)
    {
        client.Setup(c => c.RefreshAccessTokenAsync("dead-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TrueLayerException(
                "TRUELAYER_TOKEN_ERROR", "TrueLayer token endpoint error (400): {\"error\":\"invalid_grant\"}", 400));
        client.Setup(c => c.RefreshAccessTokenAsync("healthy-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerTokenSet(HealthyAccessToken, "healthy-token", 3600));
        client.Setup(c => c.ListAccountsAsync(HealthyAccessToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TrueLayerAccountInfo("brand-new-acc", "Checking", "EUR", "Sandbox", "checking", null, "9876")]);
        client.Setup(c => c.GetBalanceAsync(HealthyAccessToken, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerAccountBalance(10m, 10m, "EUR"));
    }

    [DockerRequiredFact]
    public async Task Rejected_refresh_expires_the_connection_flags_its_accounts_and_discovery_continues()
    {
        var dead = LinkedConnection(_userId, "dead", DeadMarker);
        var healthy = LinkedConnection(_userId, "healthy", HealthyMarker);
        var deadAccount = AccountOn(_userId, dead);
        var deadSibling = AccountOn(_userId, dead);
        var healthyAccount = AccountOn(_userId, healthy);
        await using (var seed = CreateContext())
        {
            seed.AddRange(dead, healthy, deadAccount, deadSibling, healthyAccount);
            await seed.SaveChangesAsync();
        }

        // One scoped context for the whole pass, like the DI container gives the real services.
        await using var context = CreateContext();
        var (sut, client) = BuildSut(context);
        StubProvider(client);

        var created = await sut.DiscoverNewAccountsAsync();

        created.Should().Be(1, "the healthy connection behind the dead one is still discovered");
        await using var verify = CreateContext();
        var connections = await verify.TrueLayerConnections.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .ToDictionaryAsync(c => c.Id);
        connections[dead.Id].Status.Should().Be("EXPIRED");
        connections[healthy.Id].Status.Should().Be("LINKED");
        connections[dead.Id].EncryptedRefreshToken.Should().Equal(new[] { DeadMarker }, "expiry must not rewrite the credential columns");

        var accounts = await verify.BankAccounts.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .ToDictionaryAsync(a => a.Id);
        accounts[deadAccount.Id].SyncStatus.Should().Be("reauth_required");
        accounts[deadSibling.Id].SyncStatus.Should().Be("reauth_required");
        accounts[healthyAccount.Id].SyncStatus.Should().NotBe("reauth_required");

        // The next scheduled pass no longer revisits the expired connection.
        await using var secondPassContext = CreateContext();
        var (secondPass, secondClient) = BuildSut(secondPassContext);
        StubProvider(secondClient);
        await secondPass.DiscoverNewAccountsAsync();
        secondClient.Verify(c => c.RefreshAccessTokenAsync("dead-token", It.IsAny<CancellationToken>()), Times.Never);
    }

    [DockerRequiredFact]
    public async Task Other_failures_do_not_expire_the_connection()
    {
        var flaky = LinkedConnection(_userId, "flaky", HealthyMarker);
        var account = AccountOn(_userId, flaky);
        await using (var seed = CreateContext())
        {
            seed.AddRange(flaky, account);
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var (sut, client) = BuildSut(context);
        client.Setup(c => c.RefreshAccessTokenAsync("healthy-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TrueLayerException("TRUELAYER_TOKEN_ERROR", "TrueLayer token endpoint error (503): unavailable", 503));

        var created = await sut.DiscoverNewAccountsAsync();

        created.Should().Be(0);
        await using var verify = CreateContext();
        (await verify.TrueLayerConnections.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .SingleAsync(c => c.Id == flaky.Id)).Status.Should().Be("LINKED");
        (await verify.BankAccounts.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .SingleAsync(a => a.Id == account.Id)).SyncStatus.Should().NotBe("reauth_required");
    }
}
