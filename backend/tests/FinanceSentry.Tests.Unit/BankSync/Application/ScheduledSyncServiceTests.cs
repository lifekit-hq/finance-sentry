namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Infrastructure.Encryption;
using Hangfire;
using FinanceSentry.Infrastructure.Logging;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for ScheduledSyncService (T313), exercised through the TrueLayer provider path.
/// All external dependencies are mocked; no database or network required.
/// </summary>
public class ScheduledSyncServiceTests
{
    // ── Shared test data ────────────────────────────────────────────────────
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    // ── Mocks + SUT factory ─────────────────────────────────────────────────

    private sealed record Harness(
        ScheduledSyncService Sut,
        Mock<IBankAccountRepository> AccountRepo,
        Mock<ITransactionRepository> TxRepo,
        Mock<ISyncJobRepository> JobRepo,
        Mock<ICredentialEncryptionService> Encryption,
        Mock<ITransactionDeduplicationService> Dedup,
        Mock<IBankProviderFactory> ProviderFactory,
        Mock<ITrueLayerConnectionRepository> TrueLayerConnections,
        Mock<ITrueLayerClient> TrueLayerClient,
        Mock<IBankProvider> Provider,
        Mock<FinanceSentry.Core.Cqrs.IEventBus> EventBus,
        BankAccount Account,
        TrueLayerConnection Connection);

    private static Harness BuildSut(
        Mock<FinanceSentry.Core.Interfaces.IAlertGeneratorService>? alertGen = null,
        Mock<FinanceSentry.Core.Interfaces.IUserAlertPreferencesReader>? userPrefs = null)
    {
        var accountRepo = new Mock<IBankAccountRepository>();
        var txRepo = new Mock<ITransactionRepository>();
        txRepo.Setup(r => r.GetAllUniqueHashesByAccountIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<string>());
        var jobRepo = new Mock<ISyncJobRepository>();
        var encryption = new Mock<ICredentialEncryptionService>();
        var dedup = new Mock<ITransactionDeduplicationService>();
        var logger = new Mock<IBankSyncLogger>();

        var providerFactory = new Mock<IBankProviderFactory>();
        var monobankCreds = new Mock<IMonobankCredentialRepository>();
        var truelayerConnections = new Mock<ITrueLayerConnectionRepository>();
        var truelayerClient = new Mock<ITrueLayerClient>();
        var monobankBalanceCache = new FinanceSentry.Modules.BankSync.Infrastructure.Monobank.MonobankBalanceCache();
        alertGen ??= new Mock<FinanceSentry.Core.Interfaces.IAlertGeneratorService>();
        userPrefs ??= new Mock<FinanceSentry.Core.Interfaces.IUserAlertPreferencesReader>();
        var eventBus = new Mock<FinanceSentry.Core.Cqrs.IEventBus>();

        var sut = new ScheduledSyncService(
            accountRepo.Object, txRepo.Object, jobRepo.Object,
            encryption.Object, dedup.Object, logger.Object,
            providerFactory.Object, monobankCreds.Object,
            truelayerConnections.Object, truelayerClient.Object,
            monobankBalanceCache,
            alertGen.Object, userPrefs.Object, eventBus.Object);

        // Default TrueLayer wiring: a linked connection with a decryptable refresh token that
        // exchanges for an access token without rotating, and a provider resolvable by name.
        var connection = new TrueLayerConnection(UserId, "ob-testbank", "Test Bank", $"ref-{Guid.NewGuid():N}");
        connection.SetRefreshToken([1], [2], [3], 1);

        var account = new BankAccount
        {
            UserId = UserId,
            Provider = "truelayer",
            ExternalAccountId = "tl-acc-1",
            TrueLayerConnectionId = connection.Id,
            BankName = "Test Bank",
            AccountType = "checking",
            Currency = "EUR",
            SyncStatus = "active",
        };

        accountRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(account);
        accountRepo.Setup(r => r.UpdateAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>())).ReturnsAsync(account);
        jobRepo.Setup(r => r.AddAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((SyncJob j, CancellationToken _) => j);
        jobRepo.Setup(r => r.UpdateAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((SyncJob j, CancellationToken _) => j);
        truelayerConnections.Setup(r => r.GetByIdAsync(connection.Id, It.IsAny<CancellationToken>()))
                            .ReturnsAsync(connection);
        encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>()))
                  .Returns("refresh-token");
        truelayerClient.Setup(c => c.RefreshAccessTokenAsync("refresh-token", It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new TrueLayerTokenSet("access-token", "refresh-token", 3600));
        txRepo.Setup(r => r.GetByAccountIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([]);

        var provider = new Mock<IBankProvider>();
        providerFactory.Setup(f => f.Resolve("truelayer")).Returns(provider.Object);

        return new Harness(sut, accountRepo, txRepo, jobRepo, encryption, dedup,
            providerFactory, truelayerConnections, truelayerClient, provider, eventBus, account, connection);
    }

    private static void SetupProviderCandidates(Harness h, IReadOnlyList<TransactionCandidate> candidates)
        => h.Provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((candidates, (DateTime?)null));

    // ── T313-1: Account not found ───────────────────────────────────────────

    [Fact]
    public async Task PerformFullSyncAsync_AccountNotFound_ReturnsFailure()
    {
        var h = BuildSut();

        h.AccountRepo.Setup(r => r.GetByIdAsync(AccountId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync((BankAccount?)null);

        var result = await h.Sut.PerformFullSyncAsync(AccountId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ACCOUNT_NOT_FOUND");
    }

    // ── T313-2: Successful sync ─────────────────────────────────────────────

    [Fact]
    public async Task PerformFullSyncAsync_HappyPath_CreatesJobFetchesAndSavesTransactions()
    {
        var h = BuildSut();

        var candidates = new List<TransactionCandidate>
        {
            new(h.Account.Id, UserId, 50m, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1),
                "Coffee", false, "debit", "Starbucks", "food"),
            new(h.Account.Id, UserId, 100m, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-2),
                "Salary", false, "credit", null, "income")
        };
        SetupProviderCandidates(h, candidates);

        h.Dedup.Setup(d => d.FilterDuplicates(
                It.IsAny<IEnumerable<TransactionCandidate>>(),
                It.IsAny<IReadOnlySet<string>>()))
             .Returns(candidates);

        var entity1 = new Transaction(h.Account.Id, UserId, 50m, DateTime.UtcNow.AddDays(-1), "Coffee", "hash1", false);
        var entity2 = new Transaction(h.Account.Id, UserId, 100m, DateTime.UtcNow.AddDays(-2), "Salary", "hash2", false);
        h.Dedup.Setup(d => d.ToEntity(candidates[0])).Returns(entity1);
        h.Dedup.Setup(d => d.ToEntity(candidates[1])).Returns(entity2);

        h.TxRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Transaction>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<Transaction> txs, CancellationToken _) => txs);

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeTrue();
        result.TransactionCountFetched.Should().Be(2);
        result.TransactionCountDeduped.Should().Be(2);

        h.JobRepo.Verify(r => r.AddAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()), Times.Once);
        h.TxRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<Transaction>>(), It.IsAny<CancellationToken>()), Times.Once);
        h.AccountRepo.Verify(r => r.UpdateAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    // ── In-batch duplicate hashes must not be double-inserted ───────────────

    [Fact]
    public async Task PerformFullSyncAsync_InBatchDuplicateHashes_InsertedOnce()
    {
        // Regression: two candidates in one provider batch that hash alike (e.g. a pending and a
        // booked copy) previously both reached AddRange and violated the unique (AccountId,
        // UniqueHash) index, poisoning the whole SaveChanges and wedging the account in "syncing".
        var h = BuildSut();

        var candidates = new List<TransactionCandidate>
        {
            new(h.Account.Id, UserId, 50m, DateTime.UtcNow.AddDays(-1), null, "Coffee", true, "debit", null, null),
            new(h.Account.Id, UserId, 50m, DateTime.UtcNow.AddDays(-1), null, "Coffee", false, "debit", null, null)
        };
        SetupProviderCandidates(h, candidates);

        h.Dedup.Setup(d => d.FilterDuplicates(It.IsAny<IEnumerable<TransactionCandidate>>(), It.IsAny<IReadOnlySet<string>>()))
             .Returns(candidates);
        // Both candidates map to entities with the SAME hash — the in-batch collision.
        h.Dedup.Setup(d => d.ToEntity(candidates[0]))
             .Returns(new Transaction(h.Account.Id, UserId, 50m, DateTime.UtcNow.AddDays(-1), "Coffee", "dup_hash", false));
        h.Dedup.Setup(d => d.ToEntity(candidates[1]))
             .Returns(new Transaction(h.Account.Id, UserId, 50m, DateTime.UtcNow.AddDays(-1), "Coffee", "dup_hash", false));

        IEnumerable<Transaction>? added = null;
        h.TxRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Transaction>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((IEnumerable<Transaction> txs, CancellationToken _) => txs)
              .Callback((IEnumerable<Transaction> txs, CancellationToken _) => added = txs.ToList());

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeTrue();
        added.Should().NotBeNull();
        added!.Should().ContainSingle("in-batch duplicate hashes collapse to one row before insert");
    }

    [Fact]
    public async Task PerformFullSyncAsync_DedupSetIncludesSoftDeletedHashes()
    {
        // The hash set handed to dedup must come from GetAllUniqueHashesByAccountIdAsync (which
        // includes soft-deleted rows), not just the active rows — otherwise a re-synced
        // soft-deleted transaction slips through and collides with the unique index.
        var h = BuildSut();

        SetupProviderCandidates(h, []);
        h.TxRepo.Setup(r => r.GetAllUniqueHashesByAccountIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(["soft_deleted_hash"]);

        IReadOnlySet<string>? seenSet = null;
        h.Dedup.Setup(d => d.FilterDuplicates(It.IsAny<IEnumerable<TransactionCandidate>>(), It.IsAny<IReadOnlySet<string>>()))
             .Callback((IEnumerable<TransactionCandidate> _, IReadOnlySet<string> hashes) => seenSet = hashes)
             .Returns([]);

        await h.Sut.PerformFullSyncAsync(h.Account.Id);

        seenSet.Should().NotBeNull();
        seenSet!.Should().Contain("soft_deleted_hash");
    }

    // ── T313-3: Deduplication filters existing transactions ─────────────────

    [Fact]
    public async Task PerformFullSyncAsync_DuplicatesFiltered_SavesOnlyNewTransactions()
    {
        var h = BuildSut();

        var allCandidates = new List<TransactionCandidate>
        {
            new(h.Account.Id, UserId, 50m, DateTime.UtcNow.AddDays(-1), null, "Coffee", true, "debit", null, null),
            new(h.Account.Id, UserId, 100m, DateTime.UtcNow.AddDays(-2), null, "Salary", true, "credit", null, null)
        };
        SetupProviderCandidates(h, allCandidates);

        // Only one existing transaction in DB
        var existingTx = new Transaction(h.Account.Id, UserId, 50m, DateTime.UtcNow.AddDays(-1), "Coffee", "hash_existing", false);
        h.TxRepo.Setup(r => r.GetByAccountIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([existingTx]);

        // Dedup returns only the NEW candidate (the existing one is filtered out)
        var newCandidates = allCandidates.Skip(1).ToList();
        h.Dedup.Setup(d => d.FilterDuplicates(
                It.IsAny<IEnumerable<TransactionCandidate>>(),
                It.IsAny<IReadOnlySet<string>>()))
             .Returns(newCandidates);

        var newEntity = new Transaction(h.Account.Id, UserId, 100m, DateTime.UtcNow.AddDays(-2), "Salary", "hash_new", false);
        h.Dedup.Setup(d => d.ToEntity(newCandidates[0])).Returns(newEntity);

        h.TxRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Transaction>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((IEnumerable<Transaction> txs, CancellationToken _) => txs);

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeTrue();
        result.TransactionCountFetched.Should().Be(2);  // 2 from the provider
        result.TransactionCountDeduped.Should().Be(1);  // 1 new after dedup
    }

    // ── Successful sync publishes AccountSyncCompletedEvent ─────────────────

    [Fact]
    public async Task PerformFullSyncAsync_Success_PublishesSyncCompletedEvent()
    {
        // Regression: the event was defined and handled but never published, so the
        // intraday snapshot refresh (FirstSyncSnapshotTrigger) was dead code.
        var h = BuildSut();
        SetupProviderCandidates(h, []);
        h.Dedup.Setup(d => d.FilterDuplicates(It.IsAny<IEnumerable<TransactionCandidate>>(), It.IsAny<IReadOnlySet<string>>()))
             .Returns([]);

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeTrue();
        h.EventBus.Verify(b => b.Publish(
            It.Is<FinanceSentry.Modules.BankSync.Domain.Events.AccountSyncCompletedEvent>(e =>
                e.AccountId == h.Account.Id && e.UserId == UserId && e.Status == "success"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PerformFullSyncAsync_Failure_DoesNotPublishSyncCompletedEvent()
    {
        var h = BuildSut();
        h.Provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SERVER_ERROR: boom"));

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeFalse();
        h.EventBus.Verify(b => b.Publish(
            It.IsAny<FinanceSentry.Modules.BankSync.Domain.Events.AccountSyncCompletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Same-hash settlement: a cleared hold flips the stored pending row ───

    [Fact]
    public async Task PerformFullSyncAsync_PostedTwinWithSameHash_SettlesStoredPendingRow()
    {
        // Monobank holds keep their date when they clear, so the settled version hashes
        // identically to the stored pending row. Dedup then drops the settled copy — the
        // stored row must be flipped to posted in place, or it stays pending forever.
        var h = BuildSut();

        var txDate = DateTime.UtcNow.AddDays(-1);
        var pendingRow = new Transaction(h.Account.Id, UserId, 50m, txDate, "Coffee", "same_hash", isPending: true);
        h.TxRepo.Setup(r => r.GetByAccountIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([pendingRow]);
        h.TxRepo.Setup(r => r.GetAllUniqueHashesByAccountIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(["same_hash"]);

        var settledCandidate = new TransactionCandidate(
            h.Account.Id, UserId, 50m, txDate, txDate, "Coffee", IsPending: false, "debit", null, null);
        SetupProviderCandidates(h, [settledCandidate]);

        h.Dedup.Setup(d => d.ComputeHash(h.Account.Id, 50m, It.IsAny<DateTime>(), "Coffee"))
             .Returns("same_hash");
        // The settled copy is a duplicate by hash, so dedup filters it out.
        h.Dedup.Setup(d => d.FilterDuplicates(It.IsAny<IEnumerable<TransactionCandidate>>(), It.IsAny<IReadOnlySet<string>>()))
             .Returns([]);

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeTrue();
        pendingRow.IsPending.Should().BeFalse("the cleared hold must settle the stored row in place");
        pendingRow.PostedDate.Should().Be(txDate);
        h.TxRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── T313-4: Hard failure during sync marks job + account failed and alerts ──

    [Fact]
    public async Task PerformFullSyncAsync_ProviderThrowsHardError_MarksFailedAndFiresAlert()
    {
        var alertGen = new Mock<FinanceSentry.Core.Interfaces.IAlertGeneratorService>();
        var userPrefs = new Mock<FinanceSentry.Core.Interfaces.IUserAlertPreferencesReader>();
        userPrefs.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new FinanceSentry.Core.Interfaces.UserAlertPreferences(false, 0m, true));

        var h = BuildSut(alertGen, userPrefs);
        h.Account.BeginSync();
        h.Account.MarkActive(1000m);

        h.Provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("INVALID_CREDENTIALS: bad token"));

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_CREDENTIALS");
        h.JobRepo.Verify(r => r.UpdateAsync(It.Is<SyncJob>(j => j.Status == "failed"), It.IsAny<CancellationToken>()), Times.Once);
        h.Account.SyncStatus.Should().Be("failed");
        alertGen.Verify(a => a.GenerateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── T313-4b: Transient throttle (429) does not fail the account or alert ────

    [Fact]
    public async Task PerformFullSyncAsync_RateLimited_KeepsAccountActiveAndSkipsAlert()
    {
        var alertGen = new Mock<FinanceSentry.Core.Interfaces.IAlertGeneratorService>();
        var userPrefs = new Mock<FinanceSentry.Core.Interfaces.IUserAlertPreferencesReader>();
        userPrefs.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new FinanceSentry.Core.Interfaces.UserAlertPreferences(false, 0m, true));

        var h = BuildSut(alertGen, userPrefs);
        h.Account.BeginSync();
        h.Account.MarkActive(1000m);

        h.Provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("RATE_LIMIT_EXCEEDED: too many requests"));

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("RATE_LIMIT_EXCEEDED");
        // Job still records the failure for observability...
        h.JobRepo.Verify(r => r.UpdateAsync(It.Is<SyncJob>(j => j.Status == "failed"), It.IsAny<CancellationToken>()), Times.Once);
        // ...but the account self-heals to active and no false alarm is raised.
        h.Account.SyncStatus.Should().Be("active");
        h.Account.LastSyncError.Should().BeNull();
        alertGen.Verify(a => a.GenerateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Balance refresh keeps the prior value when the endpoint trips ───────

    [Fact]
    public async Task SyncTrueLayer_BalanceEndpointFails_KeepsPriorBalance()
    {
        // Regression: a failed /balance call used to zero the account, recording a phantom
        // drop in every consumer (net-worth snapshots included).
        var h = BuildSut();
        h.Account.CurrentBalance = 123.45m;

        SetupProviderCandidates(h, []);
        h.Dedup.Setup(d => d.FilterDuplicates(It.IsAny<IEnumerable<TransactionCandidate>>(), It.IsAny<IReadOnlySet<string>>()))
             .Returns([]);
        h.TrueLayerClient
            .Setup(c => c.GetBalanceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TrueLayerException("TRUELAYER_ERROR", "balance endpoint down", 500));

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeTrue();
        h.Account.CurrentBalance.Should().Be(123.45m);
    }

    // ── Card accounts route balance through the /cards endpoint family ──────

    [Fact]
    public async Task SyncTrueLayer_CardAccount_UsesCardBalanceAndHealsCreditLimit()
    {
        var h = BuildSut();
        h.Account.ProductType = TrueLayerAdapter.CardProductType;
        h.Account.AccountType = "credit";

        SetupProviderCandidates(h, []);
        h.Dedup.Setup(d => d.FilterDuplicates(It.IsAny<IEnumerable<TransactionCandidate>>(), It.IsAny<IReadOnlySet<string>>()))
             .Returns([]);
        h.TrueLayerClient
            .Setup(c => c.GetCardBalanceAsync("access-token", h.Account.ExternalAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerCardBalance(Current: 250m, Available: 3750m, CreditLimit: 4000m, Currency: "EUR"));

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeTrue();
        h.Account.CurrentBalance.Should().Be(250m, "a card's current balance is the amount owed");
        h.Account.CreditLimit.Should().Be(4000m);
        h.TrueLayerClient.Verify(
            c => c.GetBalanceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Card discovery: a card added after consent appears without a reconnect ──

    [Fact]
    public async Task SyncTrueLayer_DiscoversNewCard_CreatesCreditAccountUnderSameBank()
    {
        var h = BuildSut();
        // Discovery only runs through the real adapter (it needs the /cards endpoint family).
        var adapter = new TrueLayerAdapter(
            h.TrueLayerClient.Object,
            new FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping.TrueLayerCategoryMapper(),
            Infrastructure.StubCategoryResolver.Categorizer,
            Infrastructure.StubActiveSubscriptionsReader.Empty);
        h.ProviderFactory.Setup(f => f.Resolve("truelayer")).Returns(adapter);

        h.TrueLayerClient.Setup(c => c.GetTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        h.TrueLayerClient.Setup(c => c.GetPendingTransactionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        h.TrueLayerClient.Setup(c => c.GetBalanceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerAccountBalance(100m, 100m, "EUR"));
        h.TrueLayerClient.Setup(c => c.ListCardsAsync("access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TrueLayerAccountInfo("tl-card-1", "Revolut Credit", "EUR", "REVOLUT-IE", "credit", null, "4321")]);
        h.TrueLayerClient.Setup(c => c.GetCardBalanceAsync("access-token", "tl-card-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerCardBalance(Current: 250m, Available: 3750m, CreditLimit: 4000m, Currency: "EUR"));

        h.AccountRepo.Setup(r => r.GetByExternalAccountIdAsync("tl-card-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankAccount?)null);
        h.Dedup.Setup(d => d.FilterDuplicates(It.IsAny<IEnumerable<TransactionCandidate>>(), It.IsAny<IReadOnlySet<string>>()))
            .Returns([]);

        BankAccount? created = null;
        h.AccountRepo.Setup(r => r.AddAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()))
            .Callback((BankAccount a, CancellationToken _) => created = a)
            .ReturnsAsync((BankAccount a, CancellationToken _) => a);

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeTrue();
        created.Should().NotBeNull("the unseen card must be created during sync");
        created!.AccountType.Should().Be("credit");
        created.ProductType.Should().Be(TrueLayerAdapter.CardProductType);
        created.CurrentBalance.Should().Be(250m);
        created.CreditLimit.Should().Be(4000m);
        created.BankName.Should().Be(h.Account.BankName, "the card must group under the same institution");
        created.TrueLayerConnectionId.Should().Be(h.Connection.Id);
    }

    // ── Unknown provider is an explicit failure, not a silent fallback ──────

    [Fact]
    public async Task PerformFullSyncAsync_UnknownProvider_FailsExplicitly()
    {
        var h = BuildSut();
        h.Account.Provider = "not-a-provider";

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unknown provider");
        h.JobRepo.Verify(r => r.UpdateAsync(It.Is<SyncJob>(j => j.Status == "failed"), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── T313-5: Idempotency — coordinator blocks concurrent runs ────────────

    [Fact]
    public async Task TriggerScheduledSyncAsync_AlreadyRunning_ReturnsEarlyWithoutNewSync()
    {
        var syncJobRepo = new Mock<ISyncJobRepository>();
        var syncService = new Mock<IScheduledSyncService>();

        syncJobRepo.Setup(r => r.HasRunningJobAsync(AccountId, default)).ReturnsAsync(true);

        var coordinator = new TransactionSyncCoordinator(
            syncJobRepo.Object, new Mock<IBankAccountRepository>().Object, syncService.Object,
            new Mock<IBackgroundJobClient>().Object);

        var result = await coordinator.TriggerScheduledSyncAsync(AccountId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("SYNC_IN_PROGRESS");
        syncService.Verify(
            s => s.PerformFullSyncAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
            Times.Never);
    }

    // A consent-expired account (reauth_required) must be skipped by the recurring scheduler so it stops
    // failing every cycle — the loop that logged errorCode=UNKNOWN ~48x/day for an expired TrueLayer consent.
    [Fact]
    public async Task TriggerScheduledSyncAsync_ReauthRequiredAccount_SkipsWithoutSyncing()
    {
        var syncJobRepo = new Mock<ISyncJobRepository>();
        var accountRepo = new Mock<IBankAccountRepository>();
        var syncService = new Mock<IScheduledSyncService>();

        syncJobRepo.Setup(r => r.HasRunningJobAsync(AccountId, default)).ReturnsAsync(false);
        var account = new BankAccount { Provider = "truelayer" };
        account.BeginSync();
        account.MarkReauthRequired();
        accountRepo.Setup(r => r.GetByIdAsync(AccountId, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var coordinator = new TransactionSyncCoordinator(
            syncJobRepo.Object, accountRepo.Object, syncService.Object,
            new Mock<IBackgroundJobClient>().Object);

        var result = await coordinator.TriggerScheduledSyncAsync(AccountId);

        result.ErrorCode.Should().Be("ITEM_LOGIN_REQUIRED");
        syncService.Verify(
            s => s.PerformFullSyncAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
            Times.Never);
    }

    // Regression: TrueLayer rotates the refresh_token on every refresh. The new token MUST be persisted
    // before the transaction fetch, so a mid-sync failure can't strand a consumed token and brick the
    // connection (the invalid_grant root cause). Here the provider sync throws — the rotated token must
    // still have been saved.
    [Fact]
    public async Task SyncTrueLayer_PersistsRotatedRefreshToken_EvenWhenSyncFails()
    {
        var h = BuildSut();

        h.Encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>()))
                    .Returns("old-refresh");
        h.Encryption.Setup(e => e.Encrypt("new-refresh")).Returns(new EncryptionResult([9], [8], [7], 1));
        h.TrueLayerClient
            .Setup(c => c.RefreshAccessTokenAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrueLayerTokenSet("AT", "new-refresh", 3600));

        h.Provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("mid-sync failure"));

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeFalse("the transaction fetch threw");
        h.TrueLayerConnections.Verify(
            r => r.UpdateAsync(
                It.Is<TrueLayerConnection>(c => c.EncryptedRefreshToken.Length == 1 && c.EncryptedRefreshToken[0] == 9),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "the rotated refresh token must be persisted before the failing transaction fetch");
    }
}
