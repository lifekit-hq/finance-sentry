namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for AccountDiscoveryService (fs-494): re-lists provider accounts per connection and
/// creates BankAccount rows for entries not yet known, keyed on the provider account id.
/// </summary>
public class AccountDiscoveryServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed record Harness(
        AccountDiscoveryService Sut,
        Mock<ITrueLayerConnectionRepository> TrueLayerConnections,
        Mock<ITrueLayerClient> TrueLayerClient,
        Mock<ITrueLayerTokenRefreshService> TrueLayerTokenRefresh,
        Mock<IMonobankCredentialRepository> MonobankCredentials,
        Mock<IMonobankAdapter> MonobankAdapter,
        Mock<ICredentialEncryptionService> Encryption,
        Mock<IBankAccountRepository> Accounts,
        Mock<IBackgroundJobClient> BackgroundJobs,
        List<BankAccount> AddedAccounts);

    private static Harness BuildSut()
    {
        var trueLayerConnections = new Mock<ITrueLayerConnectionRepository>();
        var trueLayerClient = new Mock<ITrueLayerClient>();
        var trueLayerTokenRefresh = new Mock<ITrueLayerTokenRefreshService>();
        var monobankCredentials = new Mock<IMonobankCredentialRepository>();
        var monobankAdapter = new Mock<IMonobankAdapter>();
        var encryption = new Mock<ICredentialEncryptionService>();
        var accounts = new Mock<IBankAccountRepository>();
        var backgroundJobs = new Mock<IBackgroundJobClient>();
        var logger = new Mock<ILogger<AccountDiscoveryService>>();

        var addedAccounts = new List<BankAccount>();
        accounts.Setup(r => r.AddAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankAccount a, CancellationToken _) =>
            {
                addedAccounts.Add(a);
                return a;
            });

        // Default: no linked connections / credentials until a test sets them up.
        trueLayerConnections.Setup(r => r.GetAllLinkedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        monobankCredentials.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        trueLayerClient.Setup(c => c.ListCardsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        backgroundJobs
            .Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Returns("job-id");

        var sut = new AccountDiscoveryService(
            trueLayerConnections.Object,
            trueLayerClient.Object,
            trueLayerTokenRefresh.Object,
            monobankCredentials.Object,
            monobankAdapter.Object,
            encryption.Object,
            accounts.Object,
            backgroundJobs.Object,
            logger.Object);

        return new Harness(sut, trueLayerConnections, trueLayerClient, trueLayerTokenRefresh,
            monobankCredentials, monobankAdapter, encryption, accounts, backgroundJobs, addedAccounts);
    }

    private static TrueLayerConnection MakeConnection()
    {
        var connection = new TrueLayerConnection(UserId, "ob-testbank", "Test Bank", $"ref-{Guid.NewGuid():N}");
        connection.SetRefreshToken([1], [2], [3], 1);
        return connection;
    }

    private static TrueLayerAccountInfo MakeTrueLayerAccount(string accountId)
        => new(accountId, "Checking", "EUR", "Test Bank", "checking", null, "1234");

    // ── TrueLayer: one known + one new → creates exactly one row; second run creates none ──

    [Fact]
    public async Task DiscoverNewAccountsAsync_TrueLayer_CreatesOnlyTheNewAccount()
    {
        var h = BuildSut();
        var connection = MakeConnection();
        h.TrueLayerConnections.Setup(r => r.GetAllLinkedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([connection]);
        h.TrueLayerTokenRefresh
            .Setup(s => s.AcquireAccessTokenAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token");

        var known = MakeTrueLayerAccount("known-acc");
        var discovered = MakeTrueLayerAccount("new-acc");
        h.TrueLayerClient.Setup(c => c.ListAccountsAsync("access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([known, discovered]);
        h.TrueLayerClient
            .Setup(c => c.GetBalanceAsync("access-token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerAccountBalance(100m, 100m, "EUR"));

        h.Accounts.Setup(r => r.GetByExternalAccountIdAsync("known-acc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAccount { ExternalAccountId = "known-acc" });
        h.Accounts.Setup(r => r.GetByExternalAccountIdAsync("new-acc", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankAccount?)null);

        var created = await h.Sut.DiscoverNewAccountsAsync();

        created.Should().Be(1);
        h.AddedAccounts.Should().ContainSingle(a => a.ExternalAccountId == "new-acc");
        h.BackgroundJobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Once);
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_TrueLayer_SecondRunCreatesNothing()
    {
        var h = BuildSut();
        var connection = MakeConnection();
        h.TrueLayerConnections.Setup(r => r.GetAllLinkedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([connection]);
        h.TrueLayerTokenRefresh
            .Setup(s => s.AcquireAccessTokenAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token");

        var known = MakeTrueLayerAccount("known-acc");
        var discovered = MakeTrueLayerAccount("new-acc");
        h.TrueLayerClient.Setup(c => c.ListAccountsAsync("access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([known, discovered]);
        h.TrueLayerClient
            .Setup(c => c.GetBalanceAsync("access-token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerAccountBalance(100m, 100m, "EUR"));

        // After the first run both accounts are now known.
        h.Accounts.Setup(r => r.GetByExternalAccountIdAsync("known-acc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAccount { ExternalAccountId = "known-acc" });
        h.Accounts.Setup(r => r.GetByExternalAccountIdAsync("new-acc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAccount { ExternalAccountId = "new-acc" });

        var created = await h.Sut.DiscoverNewAccountsAsync();

        created.Should().Be(0);
        h.AddedAccounts.Should().BeEmpty();
    }

    // ── A provider listing failure on one connection is logged and skipped, others still run ──

    [Fact]
    public async Task DiscoverNewAccountsAsync_OneConnectionFails_OthersStillDiscovered()
    {
        var h = BuildSut();
        var failingConnection = MakeConnection();
        var healthyConnection = MakeConnection();
        h.TrueLayerConnections.Setup(r => r.GetAllLinkedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([failingConnection, healthyConnection]);

        h.TrueLayerTokenRefresh
            .Setup(s => s.AcquireAccessTokenAsync(failingConnection.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TrueLayerException("invalid_grant", "refresh failed"));
        h.TrueLayerTokenRefresh
            .Setup(s => s.AcquireAccessTokenAsync(healthyConnection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token-2");

        var discovered = MakeTrueLayerAccount("new-acc-2");
        h.TrueLayerClient.Setup(c => c.ListAccountsAsync("access-token-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync([discovered]);
        h.TrueLayerClient
            .Setup(c => c.GetBalanceAsync("access-token-2", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerAccountBalance(50m, 50m, "EUR"));
        h.Accounts.Setup(r => r.GetByExternalAccountIdAsync("new-acc-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankAccount?)null);

        var created = await h.Sut.DiscoverNewAccountsAsync();

        created.Should().Be(1);
        h.AddedAccounts.Should().ContainSingle(a => a.ExternalAccountId == "new-acc-2");
    }

    // ── Monobank: one known + one new → creates exactly one row; second run creates none ──

    [Fact]
    public async Task DiscoverNewAccountsAsync_Monobank_CreatesOnlyTheNewAccount()
    {
        var h = BuildSut();
        var credential = new MonobankCredential(UserId, [1], [2], [3], 1);
        h.MonobankCredentials.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([credential]);
        h.Encryption.Setup(e => e.Decrypt(
                credential.EncryptedToken, credential.Iv, credential.AuthTag, credential.KeyVersion))
            .Returns("mono-token");

        var known = new MonobankAccountInfo("known-mono", "Jar", "black", "1234", 980, 1000, 0);
        var discovered = new MonobankAccountInfo("new-mono", "Card", "white", "5678", 980, 2000, 0);
        h.MonobankAdapter.Setup(a => a.GetAccountsAsync("mono-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([known, discovered]);

        h.Accounts.Setup(r => r.GetByExternalAccountIdAsync("known-mono", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAccount { ExternalAccountId = "known-mono" });
        h.Accounts.Setup(r => r.GetByExternalAccountIdAsync("new-mono", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankAccount?)null);

        var created = await h.Sut.DiscoverNewAccountsAsync();

        created.Should().Be(1);
        h.AddedAccounts.Should().ContainSingle(a => a.ExternalAccountId == "new-mono");
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_Monobank_SecondRunCreatesNothing()
    {
        var h = BuildSut();
        var credential = new MonobankCredential(UserId, [1], [2], [3], 1);
        h.MonobankCredentials.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([credential]);
        h.Encryption.Setup(e => e.Decrypt(
                credential.EncryptedToken, credential.Iv, credential.AuthTag, credential.KeyVersion))
            .Returns("mono-token");

        var known = new MonobankAccountInfo("known-mono", "Jar", "black", "1234", 980, 1000, 0);
        var discovered = new MonobankAccountInfo("new-mono", "Card", "white", "5678", 980, 2000, 0);
        h.MonobankAdapter.Setup(a => a.GetAccountsAsync("mono-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([known, discovered]);

        h.Accounts.Setup(r => r.GetByExternalAccountIdAsync("known-mono", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAccount { ExternalAccountId = "known-mono" });
        h.Accounts.Setup(r => r.GetByExternalAccountIdAsync("new-mono", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAccount { ExternalAccountId = "new-mono" });

        var created = await h.Sut.DiscoverNewAccountsAsync();

        created.Should().Be(0);
        h.AddedAccounts.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_MonobankCredentialFails_DoesNotThrow()
    {
        var h = BuildSut();
        var credential = new MonobankCredential(UserId, [1], [2], [3], 1);
        h.MonobankCredentials.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([credential]);
        h.Encryption.Setup(e => e.Decrypt(
                credential.EncryptedToken, credential.Iv, credential.AuthTag, credential.KeyVersion))
            .Throws(new InvalidOperationException("bad key"));

        var act = async () => await h.Sut.DiscoverNewAccountsAsync();

        await act.Should().NotThrowAsync();
    }
}
