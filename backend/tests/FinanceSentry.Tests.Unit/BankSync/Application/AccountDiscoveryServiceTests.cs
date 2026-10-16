namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Application.Connections;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;
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
        Mock<IBankConnectionHealth> ConnectionHealth,
        MonobankBalanceCache MonobankBalanceCache,
        Mock<ILogger<AccountDiscoveryService>> Logger,
        List<BankAccount> StoredAccounts,
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
        var connectionHealth = new Mock<IBankConnectionHealth>();

        var monobankBalanceCache = new MonobankBalanceCache(TimeProvider.System);

        // Stateful fake: rows added by one run are visible to the existence check of the next.
        var storedAccounts = new List<BankAccount>();
        var addedAccounts = new List<BankAccount>();
        accounts.Setup(r => r.AddAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankAccount a, CancellationToken _) =>
            {
                storedAccounts.Add(a);
                addedAccounts.Add(a);
                return a;
            });
        accounts.Setup(r => r.GetByUserIdUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid userId, CancellationToken _) =>
                storedAccounts.Where(a => a.UserId == userId && a.IsActive).ToList());
        accounts.Setup(r => r.ExistsByExternalAccountIdUnscopedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => storedAccounts.Any(a => a.ExternalAccountId == id));

        // Default: no linked connections / credentials until a test sets them up.
        trueLayerConnections.Setup(r => r.GetAllLinkedUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        monobankCredentials.Setup(r => r.GetAllUnscopedAsync(It.IsAny<CancellationToken>()))
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
            monobankBalanceCache,
            encryption.Object,
            accounts.Object,
            backgroundJobs.Object,
            connectionHealth.Object,
            logger.Object);

        return new Harness(sut, trueLayerConnections, trueLayerClient, trueLayerTokenRefresh,
            monobankCredentials, monobankAdapter, encryption, accounts, backgroundJobs, connectionHealth, monobankBalanceCache, logger,
            storedAccounts, addedAccounts);
    }

    private static TrueLayerConnection MakeConnection()
    {
        var connection = new TrueLayerConnection(UserId, "ob-testbank", "Test Bank", $"ref-{Guid.NewGuid():N}");
        connection.SetRefreshToken([1], [2], [3], 1);
        return connection;
    }

    private static TrueLayerAccountInfo MakeTrueLayerAccount(string accountId)
        => new(accountId, "Checking", "EUR", "Test Bank", "checking", null, "1234");

    private static void SeedAccount(Harness h, string externalAccountId, bool isActive = true)
        => h.StoredAccounts.Add(new BankAccount { ExternalAccountId = externalAccountId, IsActive = isActive });

    private static TrueLayerConnection SetupTrueLayer(Harness h, params string[] accountIds)
    {
        var connection = MakeConnection();
        h.TrueLayerConnections.Setup(r => r.GetAllLinkedUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([connection]);
        h.TrueLayerTokenRefresh
            .Setup(s => s.AcquireAccessTokenAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token");
        h.TrueLayerClient.Setup(c => c.ListAccountsAsync("access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(accountIds.Select(MakeTrueLayerAccount).ToList());
        h.TrueLayerClient
            .Setup(c => c.GetBalanceAsync("access-token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerAccountBalance(100m, 100m, "EUR"));
        return connection;
    }

    private static MonobankCredential SetupMonobank(Harness h, params MonobankAccountInfo[] accounts)
    {
        var credential = new MonobankCredential(UserId, [1], [2], [3], 1);
        h.MonobankCredentials.Setup(r => r.GetAllUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([credential]);
        h.Encryption.Setup(e => e.Decrypt(
                credential.EncryptedToken, credential.Iv, credential.AuthTag, credential.KeyVersion))
            .Returns("mono-token");
        h.MonobankAdapter.Setup(a => a.GetClientInfoAsync("mono-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankClientInfo("client", "Owner", accounts));
        return credential;
    }

    // ── TrueLayer: one known + one new → first run creates exactly one row; second run creates none ──

    [Fact]
    public async Task DiscoverNewAccountsAsync_TrueLayer_CreatesOnlyTheNewAccountThenNothing()
    {
        var h = BuildSut();
        SetupTrueLayer(h, "known-acc", "new-acc");
        SeedAccount(h, "known-acc");

        var firstRun = await h.Sut.DiscoverNewAccountsAsync();
        var secondRun = await h.Sut.DiscoverNewAccountsAsync();

        firstRun.Should().Be(1);
        secondRun.Should().Be(0);
        h.AddedAccounts.Should().ContainSingle(a => a.ExternalAccountId == "new-acc");
        h.BackgroundJobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Once);
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_TrueLayer_SoftDeletedAccountIsNotRecreated()
    {
        var h = BuildSut();
        SetupTrueLayer(h, "deleted-acc");
        SeedAccount(h, "deleted-acc", isActive: false);

        var created = await h.Sut.DiscoverNewAccountsAsync();

        created.Should().Be(0);
        h.AddedAccounts.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_TrueLayer_DiscoversNewCardAsCreditAccountOnce()
    {
        var h = BuildSut();
        var connection = SetupTrueLayer(h);
        h.TrueLayerClient.Setup(c => c.ListCardsAsync("access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TrueLayerAccountInfo("tl-card-1", "Revolut Credit", "EUR", "", "credit", null, "4321")]);
        h.TrueLayerClient.Setup(c => c.GetCardBalanceAsync("access-token", "tl-card-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerCardBalance(Current: 250m, Available: 3750m, CreditLimit: 4000m, Currency: "EUR"));

        var firstRun = await h.Sut.DiscoverNewAccountsAsync();
        var secondRun = await h.Sut.DiscoverNewAccountsAsync();

        firstRun.Should().Be(1);
        secondRun.Should().Be(0);
        var card = h.AddedAccounts.Should().ContainSingle().Subject;
        card.AccountType.Should().Be("credit");
        card.ProductType.Should().Be(TrueLayerAdapter.CardProductType);
        card.CurrentBalance.Should().Be(250m);
        card.CreditLimit.Should().Be(4000m);
        card.BankName.Should().Be(connection.ProviderDisplayName, "the card must group under the same institution");
        card.TrueLayerConnectionId.Should().Be(connection.Id);
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_TrueLayer_SoftDeletedCardIsNotRecreated()
    {
        var h = BuildSut();
        SetupTrueLayer(h);
        h.TrueLayerClient.Setup(c => c.ListCardsAsync("access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TrueLayerAccountInfo("tl-card-1", "Revolut Credit", "EUR", "", "credit", null, "4321")]);
        SeedAccount(h, "tl-card-1", isActive: false);

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
        h.TrueLayerConnections.Setup(r => r.GetAllLinkedUnscopedAsync(It.IsAny<CancellationToken>()))
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

        var created = await h.Sut.DiscoverNewAccountsAsync();

        created.Should().Be(1);
        h.AddedAccounts.Should().ContainSingle(a => a.ExternalAccountId == "new-acc-2");
    }

    // ── A rejected refresh (invalid_grant) applies the credential-expiry policy ──

    [Fact]
    public async Task DiscoverNewAccountsAsync_RefreshRejectedInvalidGrant_ExpiresConnectionAndFlagsItsAccountsReauthRequired()
    {
        var h = BuildSut();
        var deadConnection = MakeConnection();
        var healthyConnection = MakeConnection();
        h.TrueLayerConnections.Setup(r => r.GetAllLinkedUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([deadConnection, healthyConnection]);

        h.TrueLayerConnections.Setup(r => r.GetByIdUnscopedAsync(deadConnection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deadConnection);

        var deadAccount = new BankAccount { TrueLayerConnectionId = deadConnection.Id, UserId = UserId, ExternalAccountId = "dead-acc" };
        var otherAccount = new BankAccount { TrueLayerConnectionId = healthyConnection.Id, UserId = UserId, ExternalAccountId = "other-acc" };
        h.Accounts.Setup(r => r.GetByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([deadAccount, otherAccount]);

        h.TrueLayerTokenRefresh
            .Setup(s => s.AcquireAccessTokenAsync(deadConnection.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TrueLayerException(
                "TRUELAYER_TOKEN_ERROR", "TrueLayer token endpoint error (400): {\"error\":\"invalid_grant\"}", 400));
        h.TrueLayerTokenRefresh
            .Setup(s => s.AcquireAccessTokenAsync(healthyConnection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token-2");
        h.TrueLayerClient.Setup(c => c.ListAccountsAsync("access-token-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeTrueLayerAccount("new-acc-2")]);
        h.TrueLayerClient
            .Setup(c => c.GetBalanceAsync("access-token-2", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerAccountBalance(50m, 50m, "EUR"));

        var created = await h.Sut.DiscoverNewAccountsAsync();

        created.Should().Be(1);
        deadConnection.Status.Should().Be("EXPIRED");
        healthyConnection.Status.Should().NotBe("EXPIRED");
        deadAccount.SyncStatus.Should().Be("reauth_required");
        otherAccount.SyncStatus.Should().NotBe("reauth_required");
        h.TrueLayerConnections.Verify(r => r.UpdateAsync(deadConnection, It.IsAny<CancellationToken>()), Times.Once);
        h.TrueLayerConnections.Verify(r => r.UpdateAsync(healthyConnection, It.IsAny<CancellationToken>()), Times.Never);

        // The rejected refresh is one failed attempt on the dead consent's health; the healthy listing is a success.
        h.ConnectionHealth.Verify(c => c.RecordConnectionFailureAsync(
            It.Is<BankConnectionRef>(r => r.Id == deadConnection.Id && r.Provider == "truelayer" && r.UserId == UserId),
            It.Is<TrueLayerException>(ex => ex.Message.Contains("invalid_grant")),
            It.IsAny<CancellationToken>()), Times.Once);
        h.ConnectionHealth.Verify(c => c.RecordConnectionSuccessAsync(
            It.Is<BankConnectionRef>(r => r.Id == healthyConnection.Id), It.IsAny<CancellationToken>()), Times.Once);
        h.ConnectionHealth.Verify(c => c.RecordConnectionSuccessAsync(
            It.Is<BankConnectionRef>(r => r.Id == deadConnection.Id), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_TrueLayerListingFails_RecordsAFailedAttemptWithoutExpiring()
    {
        var h = BuildSut();
        var connection = SetupTrueLayer(h);
        var outage = new TrueLayerException("TRUELAYER_ERROR", "TrueLayer API error (503) on /accounts", 503);
        h.TrueLayerClient.Setup(c => c.ListAccountsAsync("access-token", It.IsAny<CancellationToken>())).ThrowsAsync(outage);

        await h.Sut.DiscoverNewAccountsAsync();

        connection.Status.Should().NotBe("EXPIRED");
        h.ConnectionHealth.Verify(c => c.RecordConnectionFailureAsync(
            It.Is<BankConnectionRef>(r => r.Id == connection.Id && r.Label == connection.ProviderDisplayName),
            outage, It.IsAny<CancellationToken>()), Times.Once);
        h.ConnectionHealth.Verify(c => c.RecordConnectionSuccessAsync(
            It.IsAny<BankConnectionRef>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_Monobank_ListingRecordsASuccessOnTheCredential()
    {
        var h = BuildSut();
        var credential = SetupMonobank(h, new MonobankAccountInfo("new-mono", "Card", "white", "5678", 980, 2000, 0));

        await h.Sut.DiscoverNewAccountsAsync();

        h.ConnectionHealth.Verify(c => c.RecordConnectionSuccessAsync(
            It.Is<BankConnectionRef>(r => r.Id == credential.Id && r.Provider == "monobank" && r.Label == "Monobank"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_MonobankClientInfoFails_RecordsAFailedAttempt()
    {
        var h = BuildSut();
        var credential = SetupMonobank(h);
        var rejected = new MonobankException("MONOBANK_TOKEN_INVALID", "Invalid or expired Monobank token.", 400);
        h.MonobankAdapter.Setup(a => a.GetClientInfoAsync("mono-token", It.IsAny<CancellationToken>())).ThrowsAsync(rejected);

        await h.Sut.DiscoverNewAccountsAsync();

        h.ConnectionHealth.Verify(c => c.RecordConnectionFailureAsync(
            It.Is<BankConnectionRef>(r => r.Id == credential.Id && r.Provider == "monobank"),
            rejected, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Monobank: one known + one new → first run creates exactly one row; second run creates none ──

    [Fact]
    public async Task DiscoverNewAccountsAsync_Monobank_CreatesOnlyTheNewAccountThenNothing()
    {
        var h = BuildSut();
        SetupMonobank(h,
            new MonobankAccountInfo("known-mono", "Jar", "black", "1234", 980, 1000, 0),
            new MonobankAccountInfo("new-mono", "Card", "white", "5678", 980, 2000, 0));
        SeedAccount(h, "known-mono");

        var firstRun = await h.Sut.DiscoverNewAccountsAsync();
        var secondRun = await h.Sut.DiscoverNewAccountsAsync();

        firstRun.Should().Be(1);
        secondRun.Should().Be(0);
        h.AddedAccounts.Should().ContainSingle(a => a.ExternalAccountId == "new-mono");
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_Monobank_SoftDeletedAccountIsNotRecreated()
    {
        var h = BuildSut();
        SetupMonobank(h, new MonobankAccountInfo("deleted-mono", "Card", "white", "5678", 980, 2000, 0));
        SeedAccount(h, "deleted-mono", isActive: false);

        var created = await h.Sut.DiscoverNewAccountsAsync();

        created.Should().Be(0);
        h.AddedAccounts.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_Monobank_PrimesBalanceCacheForSiblingSyncs()
    {
        var h = BuildSut();
        SetupMonobank(h,
            new MonobankAccountInfo("known-mono", "Jar", "black", "1234", 980, 1000, 0, "black"),
            new MonobankAccountInfo("new-mono", "Card", "white", "5678", 980, 2000, 0));
        SeedAccount(h, "known-mono");

        await h.Sut.DiscoverNewAccountsAsync();

        var known = h.MonobankBalanceCache.TryGet("mono-token", "known-mono");
        known.Should().NotBeNull();
        known!.CurrentBalance.Should().Be(10m);
        known.ProductType.Should().Be("black");
        h.MonobankBalanceCache.TryGet("mono-token", "new-mono")!.CurrentBalance.Should().Be(20m);
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_Monobank_SkipsClientInfoWhenSiblingSyncJustFetchedIt()
    {
        var h = BuildSut();
        SetupMonobank(h, new MonobankAccountInfo("new-mono", "Card", "white", "5678", 980, 2000, 0));
        h.MonobankBalanceCache.Set("mono-token", "known-mono",
            new BankAccountInfo("known-mono", "Jar", "black", "1234", 10m, "UAH", "Owner"));

        var created = await h.Sut.DiscoverNewAccountsAsync();

        created.Should().Be(0);
        h.MonobankAdapter.Verify(
            a => a.GetClientInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        // Only the Dormant-cadence read; nothing is recorded for a listing that was not made.
        h.ConnectionHealth.Verify(
            c => c.DormantPausedUntil(It.IsAny<BankConnectionRef>(), It.IsAny<ConnectionHealth>()), Times.Once);
        h.ConnectionHealth.VerifyNoOtherCalls();
    }

    // ── Dormant cadence (S4): a Dormant connection of an active provider is listed once a day ──

    [Fact]
    public async Task DiscoverNewAccountsAsync_DormantConnectionsPaused_ListsNeither()
    {
        var h = BuildSut();
        SetupTrueLayer(h, "new-acc");
        SetupMonobank(h, new MonobankAccountInfo("new-mono", "Card", "white", "5678", 980, 2000, 0));
        h.ConnectionHealth
            .Setup(c => c.DormantPausedUntil(It.IsAny<BankConnectionRef>(), It.IsAny<ConnectionHealth>()))
            .Returns(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero));

        var created = await h.Sut.DiscoverNewAccountsAsync();

        created.Should().Be(0);
        h.TrueLayerClient.Verify(c => c.ListAccountsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        h.MonobankAdapter.Verify(
            a => a.GetClientInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_DormantConnectionNotPaused_IsListedAsBefore()
    {
        var h = BuildSut();
        SetupMonobank(h, new MonobankAccountInfo("new-mono", "Card", "white", "5678", 980, 2000, 0));

        var created = await h.Sut.DiscoverNewAccountsAsync();

        created.Should().Be(1);
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_MonobankCredentialFails_DoesNotThrow()
    {
        var h = BuildSut();
        var credential = new MonobankCredential(UserId, [1], [2], [3], 1);
        h.MonobankCredentials.Setup(r => r.GetAllUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([credential]);
        h.Encryption.Setup(e => e.Decrypt(
                credential.EncryptedToken, credential.Iv, credential.AuthTag, credential.KeyVersion))
            .Throws(new InvalidOperationException("bad key"));

        var act = async () => await h.Sut.DiscoverNewAccountsAsync();

        await act.Should().NotThrowAsync();
    }

    // ── Card lifecycle PR 1: identifiers refreshed on existing rows; log-only "would retire" detector ──

    private static void VerifyWouldRetireLogged(Harness h, Times times)
        => h.Logger.Verify(l => l.Log(
            LogLevel.Warning, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => $"{v}".Contains("would retire")),
            null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);

    private static BankAccount SeedOwned(
        Harness h, string externalId, Guid? monobankCredentialId = null, Guid? trueLayerConnectionId = null,
        ConnectionHealth? health = null)
    {
        var account = new BankAccount
        {
            UserId = UserId, ExternalAccountId = externalId, Provider = "x", AccountNumberLast4 = "0000",
            MonobankCredentialId = monobankCredentialId, TrueLayerConnectionId = trueLayerConnectionId,
        };
        if (health is not null) account.ApplyHealth(health);
        h.StoredAccounts.Add(account);
        return account;
    }

    private static readonly ConnectionHealth DormantGone = new()
    {
        State = ConnectionHealthState.Dormant, LastFailureClass = FailureClass.ResourceGone,
    };

    [Fact]
    public async Task DiscoverNewAccountsAsync_Monobank_WritesIbanPansAndLast4OntoExistingRow()
    {
        var h = BuildSut();
        var credential = SetupMonobank(h, new MonobankAccountInfo(
            "mono-1", "black UAH", "checking", "516875******9876", 980, 100, 0,
            Iban: "UA733220010000026201234567890", MaskedPans: ["537541******1234", "516875******9876"]));
        var row = SeedOwned(h, "mono-1", monobankCredentialId: credential.Id);

        var created = await h.Sut.DiscoverNewAccountsAsync();

        created.Should().Be(0);
        row.Iban.Should().Be("UA733220010000026201234567890");
        row.MaskedPans.Should().Equal("537541******1234", "516875******9876");
        row.AccountNumberLast4.Should().Be("9876");
        h.Accounts.Verify(r => r.SaveProviderIdentifiersUnscopedAsync(row, It.IsAny<CancellationToken>()), Times.Once);
        h.Accounts.Verify(r => r.UpdateAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_Monobank_FailedIdentifierSave_DoesNotAbortTheConnection()
    {
        var h = BuildSut();
        var credential = SetupMonobank(h,
            new MonobankAccountInfo("mono-1", "black UAH", "checking", "516875******9876", 980, 100, 0, Iban: "UA1"),
            new MonobankAccountInfo("mono-2", "white UAH", "checking", "537541******1234", 980, 100, 0));
        SeedOwned(h, "mono-1", monobankCredentialId: credential.Id);
        h.Accounts.Setup(r => r.SaveProviderIdentifiersUnscopedAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("value too long"));

        var created = await h.Sut.DiscoverNewAccountsAsync();

        created.Should().Be(1);
        h.Accounts.Verify(r => r.AddAsync(It.Is<BankAccount>(a => a.ExternalAccountId == "mono-2"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_Monobank_MissingPanKeepsTheStoredLast4_AndUnchangedRowIsNotSaved()
    {
        var h = BuildSut();
        var credential = SetupMonobank(h, new MonobankAccountInfo("mono-1", "black UAH", "checking", "0000", 980, 100, 0));
        var row = SeedOwned(h, "mono-1", monobankCredentialId: credential.Id);
        row.AccountNumberLast4 = "4321";

        await h.Sut.DiscoverNewAccountsAsync();

        row.AccountNumberLast4.Should().Be("4321");
        h.Accounts.Verify(r => r.SaveProviderIdentifiersUnscopedAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Accounts.Verify(r => r.UpdateAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_TrueLayer_WritesIbanAndCardValidToOntoExistingRows()
    {
        var h = BuildSut();
        var connection = SetupTrueLayer(h);
        h.TrueLayerClient.Setup(c => c.ListAccountsAsync("access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TrueLayerAccountInfo("tl-acc", "Checking", "EUR", "AIB", "checking", "IE29AIBK93115212345678", "5678")]);
        h.TrueLayerClient.Setup(c => c.ListCardsAsync("access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TrueLayerAccountInfo("tl-card", "Credit", "EUR", "", "credit", null, "4321", ValidTo: "2027-01")]);
        var account = SeedOwned(h, "tl-acc", trueLayerConnectionId: connection.Id);
        var card = SeedOwned(h, "tl-card", trueLayerConnectionId: connection.Id);

        await h.Sut.DiscoverNewAccountsAsync();

        account.Iban.Should().Be("IE29AIBK93115212345678");
        account.AccountNumberLast4.Should().Be("5678");
        card.CardValidTo.Should().Be("2027-01");
        card.AccountNumberLast4.Should().Be("4321");
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_Monobank_LogsWouldRetire_ForAbsentDormantResourceGoneAccount_AndChangesNothing()
    {
        var h = BuildSut();
        var credential = SetupMonobank(h, new MonobankAccountInfo("mono-live", "black UAH", "checking", "1111", 980, 100, 0));
        SeedOwned(h, "mono-live", monobankCredentialId: credential.Id);
        var gone = SeedOwned(h, "mono-gone", monobankCredentialId: credential.Id, health: DormantGone);

        await h.Sut.DiscoverNewAccountsAsync();

        VerifyWouldRetireLogged(h, Times.Once());
        gone.IsActive.Should().BeTrue();
        gone.ClosedAt.Should().BeNull();
        gone.Health.Should().Be(DormantGone);
        h.Accounts.Verify(r => r.SaveProviderIdentifiersUnscopedAsync(gone, It.IsAny<CancellationToken>()), Times.Never);
        h.Accounts.Verify(r => r.UpdateAsync(gone, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_Monobank_DoesNotLogWouldRetire_ForListedOrHealthyOrOtherFailureAccounts()
    {
        var h = BuildSut();
        var credential = SetupMonobank(h,
            new MonobankAccountInfo("mono-live", "black UAH", "checking", "1111", 980, 100, 0),
            new MonobankAccountInfo("mono-listed-dormant", "black UAH", "checking", "2222", 980, 100, 0));
        SeedOwned(h, "mono-live", monobankCredentialId: credential.Id);
        SeedOwned(h, "mono-listed-dormant", monobankCredentialId: credential.Id, health: DormantGone);
        SeedOwned(h, "mono-absent-healthy", monobankCredentialId: credential.Id);
        SeedOwned(h, "mono-absent-transient", monobankCredentialId: credential.Id,
            health: new ConnectionHealth { State = ConnectionHealthState.Dormant, LastFailureClass = FailureClass.Transient });

        await h.Sut.DiscoverNewAccountsAsync();

        VerifyWouldRetireLogged(h, Times.Never());
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_Monobank_DoesNotLogWouldRetire_WhenNoSiblingIsListed()
    {
        var h = BuildSut();
        var credential = SetupMonobank(h);
        SeedOwned(h, "mono-gone", monobankCredentialId: credential.Id, health: DormantGone);

        await h.Sut.DiscoverNewAccountsAsync();

        VerifyWouldRetireLogged(h, Times.Never());
    }

    [Fact]
    public async Task DiscoverNewAccountsAsync_TrueLayer_LogsWouldRetire_ForAbsentAccount_ButNotWhenCardsListingFailed()
    {
        var h = BuildSut();
        var connection = SetupTrueLayer(h, "tl-live");
        SeedOwned(h, "tl-live", trueLayerConnectionId: connection.Id);
        SeedOwned(h, "tl-gone", trueLayerConnectionId: connection.Id, health: DormantGone);

        await h.Sut.DiscoverNewAccountsAsync();
        VerifyWouldRetireLogged(h, Times.Once());

        h.TrueLayerClient.Setup(c => c.ListCardsAsync("access-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TrueLayerException("TRUELAYER_ERROR", "down", 503));
        await h.Sut.DiscoverNewAccountsAsync();
        VerifyWouldRetireLogged(h, Times.Once());
    }
}
