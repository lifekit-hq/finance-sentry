namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Card lifecycle against real Postgres, with a context that has no principal (as the scheduler's discovery does):
/// discovery stores the provider identifiers on rows it already holds through a targeted column write (so a sync that
/// finishes while discovery is mid-call is not reverted), marks an account the list stops naming absent, retires it
/// once its own health also says Dormant/ResourceGone, and reopens it when the list names it again.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AccountDiscoveryProviderIdentifiersTests : IAsyncLifetime
{
    private const string AccessToken = "access";
    private readonly Guid _userId = Guid.NewGuid();
    private TestDatabase? _database;
    private readonly AdjustableClock _clock = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    private readonly Mock<IAlertGeneratorService> _alerts = new();

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

    private static TrueLayerConnection LinkedConnection(Guid userId)
    {
        var connection = new TrueLayerConnection(userId, "ob-sandbox", "Sandbox", $"ref-{Guid.NewGuid():N}");
        connection.SetRefreshToken([1], [4], [5], 1);
        connection.MarkLinked(DateTime.UtcNow.AddDays(30));
        return connection;
    }

    private static BankAccount CardOn(Guid userId, TrueLayerConnection connection, string externalId, string last4 = "1111") =>
        new(userId, externalId, "Sandbox", "credit", last4, "Owner", "EUR", userId, "truelayer")
        {
            TrueLayerConnectionId = connection.Id,
            CurrentBalance = -50m,
            SyncStatus = "pending",
        };

    private (AccountDiscoveryService Sut, Mock<ITrueLayerClient> TrueLayer, Mock<IMonobankAdapter> Monobank,
        Mock<IMonobankCredentialRepository> Credentials, CapturingLogger Log) BuildSut(BankSyncDbContext context)
    {
        var connections = new TrueLayerConnectionRepository(context);
        var accounts = new BankAccountRepository(context);

        var encryption = new Mock<ICredentialEncryptionService>();
        encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .Returns("token");

        var client = new Mock<ITrueLayerClient>();
        client.Setup(c => c.RefreshAccessTokenAsync("token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerTokenSet(AccessToken, "token", 3600));
        client.Setup(c => c.ListAccountsAsync(AccessToken, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        client.Setup(c => c.ListCardsAsync(AccessToken, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<IState>())).Returns("job-id");

        var refresh = new TrueLayerTokenRefreshService(connections, encryption.Object, client.Object);
        var monobankCredentials = new Mock<IMonobankCredentialRepository>();
        monobankCredentials.Setup(r => r.GetAllUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var preferences = new Mock<IUserAlertPreferencesReader>();
        preferences.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAlertPreferences(false, 0m, true));
        var connectionHealth = BankConnectionHealthFactory.Create(
            accounts, monobankCredentials.Object, connections, new Mock<IAlertGeneratorService>().Object,
            preferences.Object, TimeProvider.System);

        var monobank = new Mock<IMonobankAdapter>();
        var log = new CapturingLogger();
        var sut = new AccountDiscoveryService(
            connections, client.Object, refresh, monobankCredentials.Object, monobank.Object,
            new MonobankBalanceCache(TimeProvider.System), encryption.Object, accounts, jobs.Object,
            connectionHealth, new AccountLifecycle(accounts, _alerts.Object, _clock, NullLogger<AccountLifecycle>.Instance), log, _clock);
        return (sut, client, monobank, monobankCredentials, log);
    }

    private async Task<BankAccount> ReloadAsync(Guid id)
    {
        await using var read = CreateContext();
        return await read.BankAccounts
            .IgnoreQueryFilters([OwnerQueryFilter.Name, BankSyncDbContext.AccountActiveFilterName])
            .AsNoTracking().SingleAsync(a => a.Id == id);
    }

    [DockerRequiredFact]
    public async Task TrueLayer_discovery_stores_iban_and_card_expiry_without_reverting_a_concurrent_sync_write()
    {
        var connection = LinkedConnection(_userId);
        var account = new BankAccount(_userId, "acc-1", "Sandbox", "checking", "0000", "Owner", "EUR", _userId, "truelayer")
        { TrueLayerConnectionId = connection.Id, CurrentBalance = 10m, SyncStatus = "pending" };
        var card = CardOn(_userId, connection, "card-1");
        await using (var seed = CreateContext())
        {
            seed.AddRange(connection, account, card);
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var (sut, client, _, _, _) = BuildSut(context);
        client.Setup(c => c.ListAccountsAsync(AccessToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TrueLayerAccountInfo("acc-1", "Checking", "EUR", "Sandbox", "checking", "IE29AIBK93115212345678", "5678")]);
        // A sync finishes while discovery waits on the provider: it writes after discovery loaded its snapshot.
        client.Setup(c => c.ListCardsAsync(AccessToken, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await using var concurrent = CreateContext();
                await concurrent.BankAccounts.IgnoreQueryFilters([OwnerQueryFilter.Name])
                    .Where(a => a.Id == card.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.SyncStatus, "active").SetProperty(a => a.CurrentBalance, -75m));
                return (IReadOnlyList<TrueLayerAccountInfo>)
                    [new TrueLayerAccountInfo("card-1", "Card", "EUR", "Sandbox", "credit", null, "2222", "2027-01")];
            });

        var created = await sut.DiscoverNewAccountsAsync();

        created.Should().Be(0);
        var storedAccount = await ReloadAsync(account.Id);
        storedAccount.Iban.Should().Be("IE29AIBK93115212345678");
        storedAccount.AccountNumberLast4.Should().Be("5678");
        var storedCard = await ReloadAsync(card.Id);
        storedCard.CardValidTo.Should().Be("2027-01");
        storedCard.AccountNumberLast4.Should().Be("2222");
        storedCard.SyncStatus.Should().Be("active", "the concurrent sync's status must survive the identifier write");
        storedCard.CurrentBalance.Should().Be(-75m, "the concurrent sync's balance must survive the identifier write");
        storedCard.ClosedAt.Should().BeNull();
        storedCard.IsActive.Should().BeTrue();
    }

    private static readonly ConnectionHealth DormantGone = new()
    {
        State = ConnectionHealthState.Dormant,
        LastFailureClass = FailureClass.ResourceGone,
        LastFailureCode = "account_not_found",
        LastFailureAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
    };

    [DockerRequiredFact]
    public async Task Discovery_without_a_principal_marks_absent_retires_on_both_signals_and_reopens_on_reappearance()
    {
        var connection = LinkedConnection(_userId);
        var listed = CardOn(_userId, connection, "card-listed");
        var gone = CardOn(_userId, connection, "card-gone", "9999");
        gone.Currency = "UAH";
        gone.ApplyHealth(DormantGone);
        var absentButHealthy = CardOn(_userId, connection, "card-healthy", "8888");
        await using (var seed = CreateContext())
        {
            seed.AddRange(connection, listed, gone, absentButHealthy);
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var (sut, client, _, _, _) = BuildSut(context);
        client.Setup(c => c.ListCardsAsync(AccessToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TrueLayerAccountInfo("card-listed", "Card", "EUR", "Sandbox", "credit", null, "1111")]);

        // First run: absent with a listed sibling is only marked.
        await sut.DiscoverNewAccountsAsync();
        (await ReloadAsync(gone.Id)).AbsentSince.Should().Be(_clock.GetUtcNow());
        (await ReloadAsync(gone.Id)).ClosedAt.Should().BeNull();
        (await ReloadAsync(listed.Id)).AbsentSince.Should().BeNull();

        // Past the window: the Dormant/ResourceGone card retires, the merely-absent healthy one does not.
        _clock.Advance(TimeSpan.FromHours(25));
        await using (var again = CreateContext())
        {
            var (sut2, client2, _, _, _) = BuildSut(again);
            client2.Setup(c => c.ListCardsAsync(AccessToken, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new TrueLayerAccountInfo("card-listed", "Card", "EUR", "Sandbox", "credit", null, "1111")]);
            await sut2.DiscoverNewAccountsAsync();
        }

        var retired = await ReloadAsync(gone.Id);
        retired.ClosedAt.Should().Be(_clock.GetUtcNow());
        retired.ClosedReason.Should().Be(AccountClosedReasons.AbsentFromList);
        retired.IsActive.Should().BeTrue("a closed account keeps IsActive so its history converts at its own currency");
        retired.Currency.Should().Be("UAH");
        var healthy = await ReloadAsync(absentButHealthy.Id);
        healthy.ClosedAt.Should().BeNull();
        healthy.AbsentSince.Should().NotBeNull();
        _alerts.Verify(a => a.GenerateAccountRetiredAlertAsync(
            _userId, gone.Id, It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);

        // The provider lists it again: it reopens by itself.
        await using (var back = CreateContext())
        {
            var (sut3, client3, _, _, _) = BuildSut(back);
            client3.Setup(c => c.ListCardsAsync(AccessToken, It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new TrueLayerAccountInfo("card-listed", "Card", "EUR", "Sandbox", "credit", null, "1111"),
                    new TrueLayerAccountInfo("card-gone", "Card", "UAH", "Sandbox", "credit", null, "9999"),
                ]);
            await sut3.DiscoverNewAccountsAsync();
        }

        var reopened = await ReloadAsync(gone.Id);
        reopened.ClosedAt.Should().BeNull();
        reopened.ClosedReason.Should().BeNull();
        reopened.AbsentSince.Should().BeNull();
        reopened.Health.LastFailureAt.Should().BeNull();
    }

    [DockerRequiredFact]
    public async Task Lifecycle_stays_silent_when_the_cards_read_fails()
    {
        var connection = LinkedConnection(_userId);
        var listed = CardOn(_userId, connection, "card-listed");
        var gone = CardOn(_userId, connection, "card-gone", "9999");
        gone.ApplyHealth(DormantGone);
        await using (var seed = CreateContext())
        {
            seed.AddRange(connection, listed, gone);
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var (sut, client, _, _, _) = BuildSut(context);
        client.Setup(c => c.ListAccountsAsync(AccessToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TrueLayerAccountInfo("card-listed", "Card", "EUR", "Sandbox", "credit", null, "1111")]);
        client.Setup(c => c.ListCardsAsync(AccessToken, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TrueLayerException("TRUELAYER_API_ERROR", "cards unavailable", 503));

        await sut.DiscoverNewAccountsAsync();

        var stored = await ReloadAsync(gone.Id);
        stored.AbsentSince.Should().BeNull();
        stored.ClosedAt.Should().BeNull();
    }

    [DockerRequiredFact]
    public async Task Exact_iban_handover_without_a_principal_retires_the_old_account_and_links_the_new_one()
    {
        const string iban = "UA213223130000026007233566777";
        var credential = new MonobankCredential(_userId, [1], [2], [3], 1);
        var live = new BankAccount(_userId, "mono-live", "Monobank", "checking", "1111", "Owner", "UAH", _userId, "monobank")
        { CurrentBalance = 5m, MonobankCredentialId = credential.Id };
        var old = new BankAccount(_userId, "mono-old", "Monobank", "checking", "2222", "Owner", "UAH", _userId, "monobank")
        { CurrentBalance = 9m, MonobankCredentialId = credential.Id, Iban = iban };
        old.ApplyHealth(new ConnectionHealth { State = ConnectionHealthState.Degraded });
        await using (var seed = CreateContext())
        {
            seed.AddRange(credential, live, old);
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var (sut, _, monobank, credentials, _) = BuildSut(context);
        credentials.Setup(r => r.GetAllUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([credential]);
        monobank.Setup(m => m.GetClientInfoAsync("token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankClientInfo("client", "Owner",
            [
                new MonobankAccountInfo("mono-live", "Black", "black", "537541******1111", 980, 100_00, 0),
                new MonobankAccountInfo("mono-new", "Black", "black", "537541******3333", 980, 100_00, 0, "black", iban, ["537541******3333"]),
            ]));

        var created = await sut.DiscoverNewAccountsAsync();

        created.Should().Be(1);
        var oldStored = await ReloadAsync(old.Id);
        oldStored.ClosedAt.Should().Be(_clock.GetUtcNow());
        oldStored.ClosedReason.Should().Be(AccountClosedReasons.Reissued);
        await using var read = CreateContext();
        var fresh = await read.BankAccounts.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking().SingleAsync(a => a.ExternalAccountId == "mono-new");
        fresh.PredecessorAccountId.Should().Be(old.Id);
        fresh.Iban.Should().Be(iban);
    }

    [DockerRequiredFact]
    public async Task Monobank_discovery_stores_iban_and_every_masked_pan_and_refreshes_the_last_four()
    {
        var account = new BankAccount(_userId, "mono-1", "Monobank", "checking", "0000", "Owner", "UAH", _userId, "monobank")
        { CurrentBalance = 5m, SyncStatus = "active" };
        var gone = new BankAccount(_userId, "mono-gone", "Monobank", "checking", "7777", "Owner", "UAH", _userId, "monobank")
        { CurrentBalance = 1m };
        var credential = new MonobankCredential(_userId, [1], [2], [3], 1);
        account.MonobankCredentialId = credential.Id;
        gone.MonobankCredentialId = credential.Id;
        gone.ApplyHealth(new ConnectionHealth { State = ConnectionHealthState.Dormant, LastFailureClass = FailureClass.ResourceGone });
        await using (var seed = CreateContext())
        {
            seed.AddRange(credential, account, gone);
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var (sut, _, monobank, credentials, _) = BuildSut(context);
        credentials.Setup(r => r.GetAllUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([credential]);
        monobank.Setup(m => m.GetClientInfoAsync("token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankClientInfo("client", "Owner",
            [
                new MonobankAccountInfo("mono-1", "Black", "black", "537541******4321", 980, 100_00, 0,
                    "black", "UA213223130000026007233566001", ["537541******4321", "444444******8765"]),
            ]));

        await sut.DiscoverNewAccountsAsync();

        var stored = await ReloadAsync(account.Id);
        stored.Iban.Should().Be("UA213223130000026007233566001");
        stored.MaskedPans.Should().Equal("537541******4321", "444444******8765");
        stored.AccountNumberLast4.Should().Be("4321");
        stored.PreviousLast4.Should().BeNull("populating the placeholder is not a card change");
        stored.PanChangedAt.Should().BeNull();
        stored.SyncStatus.Should().Be("active");
        stored.CurrentBalance.Should().Be(5m);
        var absent = await ReloadAsync(gone.Id);
        absent.IsActive.Should().BeTrue();
        absent.AbsentSince.Should().Be(_clock.GetUtcNow());
        absent.ClosedAt.Should().BeNull();
    }

    [DockerRequiredFact]
    public async Task Monobank_discovery_keeps_the_old_last_four_when_the_card_is_reissued_in_place()
    {
        var account = new BankAccount(_userId, "mono-1", "Monobank", "checking", "9312", "Owner", "UAH", _userId, "monobank")
        { CurrentBalance = 5m, SyncStatus = "active" };
        var credential = new MonobankCredential(_userId, [1], [2], [3], 1);
        account.MonobankCredentialId = credential.Id;
        await using (var seed = CreateContext())
        {
            seed.AddRange(credential, account);
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var (sut, _, monobank, credentials, _) = BuildSut(context);
        credentials.Setup(r => r.GetAllUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([credential]);
        monobank.Setup(m => m.GetClientInfoAsync("token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankClientInfo("client", "Owner",
            [
                new MonobankAccountInfo("mono-1", "Black", "black", "537541******4321", 980, 100_00, 0,
                    "black", "UA213223130000026007233566001", ["537541******4321"]),
            ]));

        await sut.DiscoverNewAccountsAsync();
        await sut.DiscoverNewAccountsAsync();

        var stored = await ReloadAsync(account.Id);
        stored.AccountNumberLast4.Should().Be("4321");
        stored.PreviousLast4.Should().Be("9312");
        stored.PanChangedAt.Should().Be(_clock.GetUtcNow());
    }

    private sealed class AdjustableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class CapturingLogger : ILogger<AccountDiscoveryService>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IEnumerable<string> Messages(LogLevel level) =>
            _entries.Where(e => e.Level == level).Select(e => e.Message).ToList();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => _entries.Add((logLevel, formatter(state, exception)));
    }
}
