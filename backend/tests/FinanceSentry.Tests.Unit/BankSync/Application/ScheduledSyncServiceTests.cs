namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Encryption;
using Hangfire;
using FinanceSentry.Infrastructure.Logging;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FinanceSentry.Modules.BankSync.Application.Connections;
using FinanceSentry.Tests.Unit.Connections;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
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
        Mock<IMonobankCredentialRepository> MonobankCredentials,
        Mock<ITrueLayerClient> TrueLayerClient,
        Mock<IBankProvider> Provider,
        Mock<FinanceSentry.Core.Cqrs.IEventBus> EventBus,
        Mock<ITrueLayerTokenRefreshService> TrueLayerTokenRefresh,
        Mock<IAlertGeneratorService> Alerts,
        FakeTimeProvider Time,
        BankAccount Account,
        TrueLayerConnection Connection,
        MonobankCredential Credential)
    {
        /// <summary>The id the connection-level alert is keyed on: the token or consent behind the account.</summary>
        public Guid ConnectionId => Account.Provider == "monobank" ? Credential.Id : Connection.Id;

        public ConnectionHealth ConnectionHealth => Account.Provider == "monobank" ? Credential.Health : Connection.Health;
    }

    private static Harness BuildSut(
        Mock<IAlertGeneratorService>? alertGen = null,
        Mock<IUserAlertPreferencesReader>? userPrefs = null,
        string provider = "truelayer",
        bool policyDriven = false)
    {
        var accountRepo = new Mock<IBankAccountRepository>();
        var txRepo = new Mock<ITransactionRepository>();
        txRepo.Setup(r => r.GetAllUniqueHashesByAccountIdUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<string>());
        var jobRepo = new Mock<ISyncJobRepository>();
        var encryption = new Mock<ICredentialEncryptionService>();
        var dedup = new Mock<ITransactionDeduplicationService>();
        var logger = new Mock<IBankSyncLogger>();

        var providerFactory = new Mock<IBankProviderFactory>();
        var monobankCreds = new Mock<IMonobankCredentialRepository>();
        var truelayerConnections = new Mock<ITrueLayerConnectionRepository>();
        var truelayerClient = new Mock<ITrueLayerClient>();
        var monobankBalanceCache = new FinanceSentry.Modules.BankSync.Infrastructure.Monobank.MonobankBalanceCache(TimeProvider.System);
        alertGen ??= new Mock<IAlertGeneratorService>();
        userPrefs ??= new Mock<IUserAlertPreferencesReader>();
        var eventBus = new Mock<FinanceSentry.Core.Cqrs.IEventBus>();
        var trueLayerTokenRefresh = new Mock<ITrueLayerTokenRefreshService>();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero));

        // Default TrueLayer wiring: a linked connection with a decryptable refresh token that
        // exchanges for an access token without rotating, and a provider resolvable by name.
        var connection = new TrueLayerConnection(UserId, "ob-testbank", "Test Bank", $"ref-{Guid.NewGuid():N}");
        connection.SetRefreshToken([1], [2], [3], 1);
        var credential = new MonobankCredential(UserId, [1], [2], [3], 1);

        var account = new BankAccount
        {
            UserId = UserId,
            Provider = provider,
            ExternalAccountId = "tl-acc-1",
            TrueLayerConnectionId = provider == "truelayer" ? connection.Id : null,
            MonobankCredentialId = provider == "monobank" ? credential.Id : null,
            BankName = provider == "monobank" ? "Monobank" : "Test Bank",
            AccountType = "checking",
            Currency = "EUR",
            SyncStatus = "active",
        };

        accountRepo.Setup(r => r.GetByIdUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(account);
        accountRepo.Setup(r => r.UpdateAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>())).ReturnsAsync(account);
        // The real claim is a conditional UPDATE that leaves the account "syncing" in the database.
        accountRepo.Setup(r => r.TryClaimSyncUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                account.BeginSync();
                return true;
            });
        jobRepo.Setup(r => r.AddAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((SyncJob j, CancellationToken _) => j);
        jobRepo.Setup(r => r.UpdateAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((SyncJob j, CancellationToken _) => j);
        truelayerConnections.Setup(r => r.GetByIdUnscopedAsync(connection.Id, It.IsAny<CancellationToken>()))
                            .ReturnsAsync(connection);
        monobankCreds.Setup(r => r.GetByIdUnscopedAsync(credential.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(credential);
        ConnectionHealthStores.Back(truelayerConnections, connection.Id, () => connection.Health, connection.ApplyHealth,
            () => connection.ConnectionExpiresAt);
        ConnectionHealthStores.Back(monobankCreds, credential.Id, () => credential.Health, credential.ApplyHealth);
        encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>()))
                  .Returns("refresh-token");
        truelayerClient.Setup(c => c.RefreshAccessTokenAsync("refresh-token", It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new TrueLayerTokenSet("access-token", "refresh-token", 3600));
        trueLayerTokenRefresh
            .Setup(s => s.AcquireAccessTokenAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token");
        txRepo.Setup(r => r.GetByAccountIdUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([]);

        var bankProvider = new Mock<IBankProvider>();
        providerFactory.Setup(f => f.Resolve(provider)).Returns(bankProvider.Object);

        // The default config keeps every provider in shadow mode; a policy-driven test lists the provider as active.
        var options = new ConnectionHealthOptions { ActiveProviders = policyDriven ? [provider] : [] };
        var connectionHealth = BankConnectionHealths.Create(
            accountRepo.Object, monobankCreds.Object, truelayerConnections.Object, alertGen.Object, userPrefs.Object, time, options);

        var sut = new ScheduledSyncService(
            accountRepo.Object, txRepo.Object, jobRepo.Object,
            encryption.Object, dedup.Object, logger.Object,
            providerFactory.Object, monobankCreds.Object,
            truelayerConnections.Object, truelayerClient.Object,
            monobankBalanceCache,
            alertGen.Object, userPrefs.Object, eventBus.Object,
            trueLayerTokenRefresh.Object, connectionHealth);

        return new Harness(sut, accountRepo, txRepo, jobRepo, encryption, dedup,
            providerFactory, truelayerConnections, monobankCreds, truelayerClient, bankProvider, eventBus,
            trueLayerTokenRefresh, alertGen, time, account, connection, credential);
    }

    private static void SetupProviderCandidates(Harness h, IReadOnlyList<TransactionCandidate> candidates)
        => h.Provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((candidates, (DateTime?)null));

    [Fact]
    public async Task PerformFullSyncAsync_ClaimRefused_ReturnsSyncInProgressWithoutCreatingAJob()
    {
        var h = BuildSut();
        h.AccountRepo.Setup(r => r.TryClaimSyncUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("SYNC_IN_PROGRESS");
        h.JobRepo.Verify(r => r.AddAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PerformFullSyncAsync_JobCreationThrowsAfterClaim_ReleasesTheClaimAndRethrows()
    {
        var h = BuildSut();
        h.JobRepo.Setup(r => r.AddAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var act = () => h.Sut.PerformFullSyncAsync(h.Account.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("db down");
        h.AccountRepo.Verify(r => r.ReleaseSyncUnscopedAsync(h.Account.Id, It.IsAny<CancellationToken>()), Times.Once);
        h.Provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PerformFullSyncAsync_CallerCancelledMidSync_StillReleasesTheClaimAndFailsTheJob()
    {
        var h = BuildSut();
        using var cts = new CancellationTokenSource();
        h.Provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });
        h.JobRepo.Setup(r => r.UpdateAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
            .Returns((SyncJob j, CancellationToken c) =>
            {
                c.ThrowIfCancellationRequested();
                return Task.FromResult(j);
            });
        h.AccountRepo.Setup(r => r.UpdateAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()))
            .Returns((BankAccount a, CancellationToken c) =>
            {
                c.ThrowIfCancellationRequested();
                return Task.FromResult(a);
            });

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id, cts.Token);

        result.Success.Should().BeFalse();
        h.Account.SyncStatus.Should().NotBe("syncing");
        h.JobRepo.Verify(r => r.UpdateAsync(
            It.Is<SyncJob>(j => j.Status == "failed"), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task PerformFullSyncAsync_JobUpdateFailsDuringFailureHandling_StillReleasesTheClaim()
    {
        var h = BuildSut();
        h.Provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider broke"));
        h.JobRepo.Setup(r => r.UpdateAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeFalse();
        h.Account.SyncStatus.Should().NotBe("syncing");
    }

    [Fact]
    public async Task PerformFullSyncAsync_FailureBookkeepingThrows_FallsBackToTheTrackerFreeRelease()
    {
        var h = BuildSut();
        h.Provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider broke"));
        h.AccountRepo.Setup(r => r.UpdateAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("tracker poisoned"));

        var result = await h.Sut.PerformFullSyncAsync(h.Account.Id);

        result.Success.Should().BeFalse();
        h.AccountRepo.Verify(r => r.ReleaseSyncUnscopedAsync(h.Account.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── T313-1: Account not found ───────────────────────────────────────────

    [Fact]
    public async Task PerformFullSyncAsync_AccountNotFound_ReturnsFailure()
    {
        var h = BuildSut();

        h.AccountRepo.Setup(r => r.TryClaimSyncUnscopedAsync(AccountId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(false);
        h.AccountRepo.Setup(r => r.GetByIdUnscopedAsync(AccountId, It.IsAny<CancellationToken>()))
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
        h.AccountRepo.Verify(r => r.UpdateAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
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
        // The hash set handed to dedup must come from GetAllUniqueHashesByAccountIdUnscopedAsync (which
        // includes soft-deleted rows), not just the active rows — otherwise a re-synced
        // soft-deleted transaction slips through and collides with the unique index.
        var h = BuildSut();

        SetupProviderCandidates(h, []);
        h.TxRepo.Setup(r => r.GetAllUniqueHashesByAccountIdUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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
        h.TxRepo.Setup(r => r.GetByAccountIdUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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
        h.TxRepo.Setup(r => r.GetByAccountIdUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([pendingRow]);
        h.TxRepo.Setup(r => r.GetAllUniqueHashesByAccountIdUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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

    // ── Failure handling, policy-driven: the provider is in ConnectionHealth:ActiveProviders (Option B, S2) ──

    private static Mock<IUserAlertPreferencesReader> SyncFailureAlertsOn()
    {
        var userPrefs = new Mock<IUserAlertPreferencesReader>();
        userPrefs.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new UserAlertPreferences(false, 0m, true));
        return userPrefs;
    }

    /// <summary>A sync of a provider listed in <c>ConnectionHealth:ActiveProviders</c> (or, with <paramref name="policyDriven"/> false, in shadow) that fails with <paramref name="ex"/>.</summary>
    private static Harness FailingWith(Exception ex, string provider = "truelayer", bool policyDriven = true)
    {
        var h = BuildSut(new Mock<IAlertGeneratorService>(), SyncFailureAlertsOn(), provider, policyDriven);
        ThrowFromProvider(h, ex);
        return h;
    }

    private static void ThrowFromProvider(Harness h, Exception ex) =>
        h.Provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

    private static void SucceedFromProvider(Harness h)
    {
        SetupProviderCandidates(h, []);
        h.Dedup.Setup(d => d.FilterDuplicates(It.IsAny<IEnumerable<TransactionCandidate>>(), It.IsAny<IReadOnlySet<string>>()))
             .Returns([]);
        h.Provider.Setup(p => p.GetAccountsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private static Task<SyncResult> SyncAsync(Harness h) => h.Sut.PerformFullSyncAsync(h.Account.Id);

    /// <summary>Runs one failing sync after each gap: the scheduler's cadence, in the harness's own clock.</summary>
    private static async Task<SyncResult> SyncAtAsync(Harness h, params TimeSpan[] gaps)
    {
        SyncResult result = await SyncAsync(h);
        foreach (var gap in gaps)
        {
            h.Time.Advance(gap);
            result = await SyncAsync(h);
        }

        return result;
    }

    private static void VerifySyncFailureAlerts(Harness h, Times times) =>
        h.Alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()), times);

    private static void VerifyNoSyncFailureAlert(Harness h)
    {
        VerifySyncFailureAlerts(h, Times.Never());
        h.Alerts.Verify(a => a.EscalateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A confirmed credential loss: raised through the escalation path, which supersedes an open outage alert.</summary>
    private static void VerifyCredentialLossEscalated(Harness h, Guid referenceId, string? code) =>
        h.Alerts.Verify(a => a.EscalateSyncFailureAlertAsync(
            UserId, h.Account.Provider, referenceId, h.Account.BankName,
            code, It.IsAny<CancellationToken>()), Times.Once);

    private static void VerifySyncFailureAlert(Harness h, Guid referenceId, string? code, SyncFailureClass failureClass) =>
        h.Alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            UserId, h.Account.Provider, referenceId, h.Account.BankName,
            code, failureClass, It.IsAny<CancellationToken>()), Times.Once);

    private static readonly TimeSpan Cycle = TimeSpan.FromMinutes(30);

    [Fact]
    public async Task HardError_FirstFailure_MarksFailedButRaisesNoAlertYet()
    {
        var h = FailingWith(new MonobankException(
            "MONOBANK_HTTP_ERROR", "Monobank API error (400): {\"errorDescription\":\"Unknown account\"}", 400), "monobank");
        h.Account.BeginSync();
        h.Account.MarkActive(1000m);

        var result = await SyncAsync(h);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("MONOBANK_HTTP_ERROR");
        h.Account.LastSyncError.Should().Contain("400").And.Contain("Unknown account");
        h.JobRepo.Verify(r => r.UpdateAsync(It.Is<SyncJob>(j => j.Status == "failed"), It.IsAny<CancellationToken>()), Times.Once);
        h.Account.SyncStatus.Should().Be("failed");
        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.Degraded);
        VerifyNoSyncFailureAlert(h);
    }

    [Fact]
    public async Task UnknownFailure_ThreeFailuresInsideTwoHours_StaySilent()
    {
        var h = FailingWith(new TrueLayerException("TRUELAYER_BAD_REQUEST", "TrueLayer API error (400)", 400));

        await SyncAtAsync(h, Cycle, Cycle, Cycle);

        h.ConnectionHealth.ConsecutiveFailures.Should().Be(4);
        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.Degraded, "4 failures in 1.5 h is not yet 2 h without success");
        VerifyNoSyncFailureAlert(h);
    }

    [Fact]
    public async Task UnknownFailure_AtThreeFailuresAndTwoHours_AlertsOnceOnTheConnection()
    {
        var h = FailingWith(new TrueLayerException("TRUELAYER_BAD_REQUEST", "TrueLayer API error (400)", 400));

        await SyncAtAsync(h, TimeSpan.FromHours(1), TimeSpan.FromMinutes(59));
        VerifyNoSyncFailureAlert(h);

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await SyncAsync(h);
        await SyncAtAsync(h, Cycle, Cycle);

        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.Failing);
        VerifySyncFailureAlert(h, h.ConnectionId, "TRUELAYER_BAD_REQUEST", SyncFailureClass.Unknown);
        VerifySyncFailureAlerts(h, Times.Once());
        h.Account.SyncStatus.Should().Be("failed", "only a confirmed credential loss parks the account");
    }

    [Fact]
    public async Task TransientFailure_PastTheThreshold_AlertsAsAnOutageAndKeepsTheAccountActive()
    {
        var h = FailingWith(new TrueLayerException("TRUELAYER_RATE_LIMITED", "TrueLayer API error (429)", 429));

        var result = await SyncAtAsync(h, Cycle, Cycle, Cycle, Cycle);

        result.ErrorCode.Should().Be("TRUELAYER_RATE_LIMITED");
        h.Account.SyncStatus.Should().Be("active");
        h.Account.LastSyncError.Should().BeNull();
        VerifySyncFailureAlert(h, h.ConnectionId, "TRUELAYER_RATE_LIMITED", SyncFailureClass.Outage);
    }

    [Fact]
    public async Task TrueLayerInvalidGrant_FlagsReauthAndAlertsAtOnceOnTheConnection()
    {
        var h = FailingWith(new TrueLayerException(
            "TRUELAYER_BAD_REQUEST", "TrueLayer token endpoint error (400): {\"error\":\"invalid_grant\"}", 400));

        var result = await SyncAsync(h);

        result.ErrorCode.Should().Be("ITEM_LOGIN_REQUIRED");
        h.Account.SyncStatus.Should().Be("reauth_required");
        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.ActionRequired);
        VerifyCredentialLossEscalated(h, h.ConnectionId, "ITEM_LOGIN_REQUIRED");
    }

    [Fact]
    public async Task MonobankTokenRejected_Once_IsOnlySuspect_TheAccountKeepsSyncingAndNoAlert()
    {
        var h = FailingWith(new MonobankException("MONOBANK_TOKEN_INVALID", "Invalid or expired Monobank token.", 400), "monobank");

        await SyncAsync(h);

        h.Account.SyncStatus.Should().Be("failed", "a suspect rejection must not park the account before it is confirmed");
        h.ConnectionHealth.SuspectSince.Should().NotBeNull();
        VerifyNoSyncFailureAlert(h);
    }

    [Fact]
    public async Task MonobankTokenRejected_AgainAfterTheConfirmationWindow_FlagsReauthAndAlertsAtOnce()
    {
        var h = FailingWith(new MonobankException("MONOBANK_TOKEN_INVALID", "Invalid or expired Monobank token.", 400), "monobank");

        await SyncAtAsync(h, Cycle);

        h.Account.SyncStatus.Should().Be("reauth_required");
        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.ActionRequired);
        VerifyCredentialLossEscalated(h, h.Credential.Id, "MONOBANK_TOKEN_INVALID");
    }

    [Fact]
    public async Task MonobankTokenRejected_ThenRecovers_ClearsTheSuspicionWithoutAnAlert()
    {
        var h = FailingWith(new MonobankException("MONOBANK_TOKEN_INVALID", "Invalid or expired Monobank token.", 400), "monobank");
        await SyncAsync(h);

        h.Time.Advance(Cycle);
        SucceedFromProvider(h);
        (await SyncAsync(h)).Success.Should().BeTrue();

        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.Healthy);
        h.ConnectionHealth.SuspectSince.Should().BeNull();
        VerifyNoSyncFailureAlert(h);
    }

    [Fact]
    public async Task TrueLayerUnauthorized_BeforeTheConsentExpires_IsOnlySuspect()
    {
        var h = FailingWith(new TrueLayerException("TRUELAYER_UNAUTHORIZED", "TrueLayer API error (401)", 401));
        h.Connection.MarkLinked(h.Time.GetUtcNow().UtcDateTime.AddDays(10));

        await SyncAsync(h);

        h.Account.SyncStatus.Should().Be("failed");
        VerifyNoSyncFailureAlert(h);
    }

    [Fact]
    public async Task TrueLayerUnauthorized_AfterTheConsentExpired_IsACredentialLossAtOnce()
    {
        var h = FailingWith(new TrueLayerException("TRUELAYER_UNAUTHORIZED", "TrueLayer API error (401)", 401));
        h.Connection.MarkLinked(h.Time.GetUtcNow().UtcDateTime.AddMinutes(-1));

        await SyncAsync(h);

        h.Account.SyncStatus.Should().Be("reauth_required");
        VerifyCredentialLossEscalated(h, h.ConnectionId, "TRUELAYER_UNAUTHORIZED");
    }

    [Fact]
    public async Task Recovery_AfterTheUserWasTold_ResolvesTheConnectionAlert()
    {
        var h = FailingWith(new TrueLayerException(
            "TRUELAYER_BAD_REQUEST", "TrueLayer token endpoint error (400): {\"error\":\"invalid_grant\"}", 400));
        await SyncAsync(h);

        SucceedFromProvider(h);
        h.Time.Advance(Cycle);
        (await h.Sut.PerformFullSyncAsync(h.Account.Id)).Success.Should().BeTrue();

        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.Healthy);
        h.Alerts.Verify(a => a.ResolveSyncFailureAlertAsync(UserId, "truelayer", h.ConnectionId, It.IsAny<CancellationToken>()), Times.Once);
        // An alert raised on the account id before the policy drove them is cleared too.
        h.Alerts.Verify(a => a.ResolveSyncFailureAlertAsync(UserId, "truelayer", h.Account.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ParseError_SaysNothingAboutTheConnection_NeverCountsOrAlerts()
    {
        var h = FailingWith(new TrueLayerException("TRUELAYER_PARSE_ERROR", "Empty body from /data/v1/accounts", 500));

        await SyncAtAsync(h, Cycle, Cycle, Cycle, Cycle, Cycle, Cycle);

        h.Account.SyncStatus.Should().Be("failed");
        h.ConnectionHealth.Should().Be(new ConnectionHealth());
        VerifyNoSyncFailureAlert(h);
    }

    [Fact]
    public async Task UnclassifiedError_OfOurOwn_StillCountsAndIsToldPastTheThreshold()
    {
        // A persistent failure on our side must not go silent: it is unknown, not internal.
        var h = FailingWith(new InvalidOperationException("boom"));

        var first = await SyncAsync(h);
        first.ErrorCode.Should().BeNull();
        h.Account.SyncStatus.Should().Be("failed");
        h.ConnectionHealth.LastFailureClass.Should().Be(FailureClass.Unknown);
        VerifyNoSyncFailureAlert(h);

        await SyncAtAsync(h, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        VerifySyncFailureAlert(h, h.ConnectionId, null, SyncFailureClass.Unknown);
    }

    [Fact]
    public async Task MissingTrueLayerConfiguration_IsOursButPersistent_SoItIsToldPastTheThreshold()
    {
        var h = FailingWith(new TrueLayerException("TRUELAYER_NOT_CONFIGURED", "TrueLayer credentials are not configured.", 503));

        await SyncAtAsync(h, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        VerifySyncFailureAlert(h, h.ConnectionId, "TRUELAYER_NOT_CONFIGURED", SyncFailureClass.Unknown);
    }

    [Fact]
    public async Task UnreadableStoredToken_IsADefinitiveCredentialLoss_ToldAtOnce()
    {
        var h = BuildSut(new Mock<IAlertGeneratorService>(), SyncFailureAlertsOn(), "monobank", policyDriven: true);
        h.Encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .Throws(new System.Security.Cryptography.AuthenticationTagMismatchException());

        var result = await SyncAsync(h);

        result.ErrorCode.Should().Be(CredentialUnreadableException.Code);
        h.Account.SyncStatus.Should().Be("reauth_required");
        VerifyCredentialLossEscalated(h, h.Credential.Id, CredentialUnreadableException.Code);
    }

    [Fact]
    public async Task TransientThenRevoked_TheCredentialAlertSupersedesTheOpenOutageAlert()
    {
        var h = FailingWith(new TrueLayerException("TRUELAYER_ERROR", "TrueLayer API error (503)", 503));
        await SyncAtAsync(h, Cycle, Cycle, Cycle, Cycle);
        VerifySyncFailureAlert(h, h.ConnectionId, "TRUELAYER_ERROR", SyncFailureClass.Outage);

        ThrowFromProvider(h, new TrueLayerException(
            "TRUELAYER_BAD_REQUEST", "TrueLayer token endpoint error (400): {\"error\":\"invalid_grant\"}", 400));
        h.Time.Advance(Cycle);
        await SyncAsync(h);

        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.ActionRequired);
        h.Account.SyncStatus.Should().Be("reauth_required");
        VerifyCredentialLossEscalated(h, h.ConnectionId, "ITEM_LOGIN_REQUIRED");
    }

    [Fact]
    public async Task FailingThenSuspectConfirmed_TheCredentialAlertSupersedesTheOpenOutageAlert()
    {
        var h = FailingWith(new TrueLayerException("TRUELAYER_ERROR", "TrueLayer API error (503)", 503));
        await SyncAtAsync(h, Cycle, Cycle, Cycle, Cycle);
        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.Failing);

        ThrowFromProvider(h, new TrueLayerException("TRUELAYER_FORBIDDEN", "TrueLayer API error (403)", 403));
        h.Time.Advance(Cycle);
        await SyncAsync(h);
        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.Failing, "one refusal is only suspect");
        h.Alerts.Verify(a => a.EscalateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        h.Time.Advance(Cycle);
        await SyncAsync(h);

        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.ActionRequired);
        VerifyCredentialLossEscalated(h, h.ConnectionId, "TRUELAYER_FORBIDDEN");
        VerifySyncFailureAlerts(h, Times.Once());
    }

    [Fact]
    public async Task CallerCancellation_SaysNothingAboutTheProvider()
    {
        var h = BuildSut(new Mock<IAlertGeneratorService>(), SyncFailureAlertsOn(), policyDriven: true);
        using var cts = new CancellationTokenSource();
        h.Provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        await h.Sut.PerformFullSyncAsync(h.Account.Id, cts.Token);

        h.Account.SyncStatus.Should().Be("active");
        h.ConnectionHealth.Should().Be(new ConnectionHealth());
        h.Account.Health.Should().Be(new ConnectionHealth());
    }

    [Fact]
    public async Task SiblingSucceededDuringTheStreak_TheFailureIsTheAccountsAlone()
    {
        var h = FailingWith(new TrueLayerException("TRUELAYER_BAD_REQUEST", "TrueLayer API error (400)", 400));
        await SyncAsync(h);
        var chargedOnce = h.ConnectionHealth;
        chargedOnce.ConsecutiveFailures.Should().Be(1, "the first failure of a streak is charged to the connection");

        // A sibling account on the same consent syncs fine.
        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.Connection.ApplyHealth(ConnectionHealthPolicy.RecordSuccess(h.Connection.Health, h.Time.GetUtcNow()).Health);
        var afterSibling = h.Connection.Health;

        await SyncAtAsync(h, Cycle, TimeSpan.FromHours(1), TimeSpan.FromMinutes(29));

        h.Connection.Health.Should().Be(afterSibling, "the consent works, so its health is not charged");
        h.Account.Health.State.Should().Be(ConnectionHealthState.Failing);
        h.Account.Health.LastFailureClass.Should().Be(FailureClass.ResourceGone);
        VerifySyncFailureAlert(h, h.Account.Id, "TRUELAYER_BAD_REQUEST", SyncFailureClass.Unknown);
        VerifySyncFailureAlerts(h, Times.Once());
    }

    [Fact]
    public async Task TransientFailure_WhileTheConnectionWorks_AlertsOnTheAccountAsAnOutage()
    {
        // Discovery's listing (or a sibling) keeps succeeding on the consent while this one account's fetch keeps timing out.
        var h = FailingWith(new TrueLayerException("TRUELAYER_ERROR", "TrueLayer API error (503) on /transactions", 503));
        await SyncAsync(h);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.Connection.ApplyHealth(ConnectionHealthPolicy.RecordSuccess(h.Connection.Health, h.Time.GetUtcNow()).Health);

        await SyncAtAsync(h, Cycle, TimeSpan.FromHours(1), TimeSpan.FromMinutes(29));

        h.Connection.Health.State.Should().Be(ConnectionHealthState.Healthy);
        h.Account.Health.LastFailureClass.Should().Be(FailureClass.Transient);
        h.Account.SyncStatus.Should().Be("active");
        VerifySyncFailureAlert(h, h.Account.Id, "TRUELAYER_ERROR", SyncFailureClass.Outage);
        VerifySyncFailureAlerts(h, Times.Once());
    }

    [Fact]
    public async Task SuspectRejection_DuringAProviderWideOutage_IsReadAsTransient()
    {
        var h = FailingWith(new TrueLayerException("TRUELAYER_FORBIDDEN", "TrueLayer API error (403)", 403));
        h.TrueLayerConnections
            .Setup(r => r.GetProviderHealthUnscopedAsync(h.Connection.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderHealthSnapshot(OtherConnectionsFailing: 2, AnyOtherConnectionSucceeded: false));

        await SyncAtAsync(h, Cycle, Cycle);

        h.ConnectionHealth.State.Should().NotBe(ConnectionHealthState.ActionRequired);
        h.ConnectionHealth.LastFailureClass.Should().Be(FailureClass.Transient);
        h.Account.SyncStatus.Should().Be("active");
        VerifyNoSyncFailureAlert(h);
    }

    [Fact]
    public async Task SuspectRejection_WhileOtherConnectionsStillSucceed_IsConfirmedAsUsual()
    {
        var h = FailingWith(new TrueLayerException("TRUELAYER_FORBIDDEN", "TrueLayer API error (403)", 403));
        h.TrueLayerConnections
            .Setup(r => r.GetProviderHealthUnscopedAsync(h.Connection.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderHealthSnapshot(OtherConnectionsFailing: 2, AnyOtherConnectionSucceeded: true));

        await SyncAtAsync(h, Cycle);

        h.Account.SyncStatus.Should().Be("reauth_required");
        VerifyCredentialLossEscalated(h, h.ConnectionId, "TRUELAYER_FORBIDDEN");
    }

    // ── Failure handling, shadow mode (the default config): alerts and status exactly as before the policy ──

    private static Harness ShadowFailingWith(Exception ex, string provider = "truelayer") =>
        FailingWith(ex, provider, policyDriven: false);

    private static void VerifyNoConnectionLevelAlert(Harness h)
    {
        h.Alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), h.ConnectionId, It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Alerts.Verify(a => a.EscalateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void DefaultConfig_KeepsMonobankAndTrueLayerInShadow()
    {
        var options = new ConnectionHealthOptions();

        options.ActiveProviders.Should().BeEmpty();
        var health = BankConnectionHealths.Create(
            Mock.Of<IBankAccountRepository>(), Mock.Of<IMonobankCredentialRepository>(), Mock.Of<ITrueLayerConnectionRepository>(),
            Mock.Of<IAlertGeneratorService>(), Mock.Of<IUserAlertPreferencesReader>(), TimeProvider.System, options);
        health.IsActive("monobank").Should().BeFalse();
        health.IsActive("truelayer").Should().BeFalse();
    }

    [Fact]
    public async Task Shadow_HardError_MarksFailedAndAlertsAtOnceOnTheAccount()
    {
        var h = ShadowFailingWith(new MonobankException(
            "MONOBANK_HTTP_ERROR", "Monobank API error (400): {\"errorDescription\":\"Unknown account\"}", 400), "monobank");

        var result = await SyncAsync(h);

        result.ErrorCode.Should().Be("MONOBANK_HTTP_ERROR");
        h.Account.LastSyncError.Should().Contain("400").And.Contain("Unknown account");
        h.Account.SyncStatus.Should().Be("failed");
        VerifySyncFailureAlert(h, h.Account.Id, "MONOBANK_HTTP_ERROR", SyncFailureClass.Unknown);
        VerifyNoConnectionLevelAlert(h);
    }

    [Fact]
    public async Task Shadow_RateLimited_KeepsTheAccountActiveAndSkipsTheAlert()
    {
        var h = ShadowFailingWith(new TrueLayerException("TRUELAYER_RATE_LIMITED", "TrueLayer API error (429)", 429));

        var result = await SyncAsync(h);

        result.ErrorCode.Should().Be("TRUELAYER_RATE_LIMITED");
        h.Account.SyncStatus.Should().Be("active");
        h.Account.LastSyncError.Should().BeNull();
        VerifyNoSyncFailureAlert(h);
    }

    [Theory]
    [InlineData("TRUELAYER_PARSE_ERROR", 500)]
    [InlineData("TRUELAYER_NOT_CONFIGURED", 503)]
    public async Task Shadow_TrueLayerLocalFailure_MarksFailedAndAlerts(string code, int status)
    {
        var h = ShadowFailingWith(new TrueLayerException(code, "local failure", status));

        await SyncAsync(h);

        h.Account.SyncStatus.Should().Be("failed");
        VerifySyncFailureAlert(h, h.Account.Id, code, SyncFailureClass.Unknown);
    }

    [Fact]
    public async Task Shadow_TrueLayerInvalidGrant_FlagsReauthAndAlertsAtOnceOnTheAccount()
    {
        var h = ShadowFailingWith(new TrueLayerException(
            "TRUELAYER_BAD_REQUEST", "TrueLayer token endpoint error (400): {\"error\":\"invalid_grant\"}", 400));

        var result = await SyncAsync(h);

        result.ErrorCode.Should().Be("ITEM_LOGIN_REQUIRED");
        h.Account.SyncStatus.Should().Be("reauth_required");
        VerifySyncFailureAlert(h, h.Account.Id, "ITEM_LOGIN_REQUIRED", SyncFailureClass.Credential);
        VerifyNoConnectionLevelAlert(h);
    }

    [Fact]
    public async Task Shadow_MonobankTokenRejected_FlagsReauthAtOnce_WhileThePolicyRecordsOnlyASuspicion()
    {
        var h = ShadowFailingWith(new MonobankException("MONOBANK_TOKEN_INVALID", "Invalid or expired Monobank token.", 400), "monobank");

        await SyncAsync(h);

        // Today's behaviour: one rejection parks the card and alerts with the reconnect copy, on the card.
        h.Account.SyncStatus.Should().Be("reauth_required");
        VerifySyncFailureAlert(h, h.Account.Id, "MONOBANK_TOKEN_INVALID", SyncFailureClass.Credential);
        VerifyNoConnectionLevelAlert(h);
        // The policy's would-be view, recorded on the token for the 2026-10-21 comparison: suspect, not yet lost.
        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.Degraded);
        h.ConnectionHealth.SuspectSince.Should().NotBeNull();
        h.Account.Health.LastFailureClass.Should().Be(FailureClass.Credential);
    }

    [Fact]
    public async Task Shadow_UnclassifiedError_AlertsWithoutTheReconnectCopy()
    {
        var h = ShadowFailingWith(new InvalidOperationException("boom"));

        await SyncAsync(h);

        h.Account.SyncStatus.Should().Be("failed");
        VerifySyncFailureAlert(h, h.Account.Id, null, SyncFailureClass.Unknown);
    }

    [Fact]
    public async Task Shadow_PastThePolicyThreshold_RaisesNothingNew_ButRecordsTheWouldBeState()
    {
        var h = ShadowFailingWith(new TrueLayerException("TRUELAYER_ERROR", "TrueLayer API error (503)", 503));

        await SyncAtAsync(h, Cycle, Cycle, Cycle, Cycle);

        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.Failing, "the would-be decision is still recorded");
        VerifyNoSyncFailureAlert(h);
    }

    [Fact]
    public async Task Shadow_ConfirmedLossInThePolicy_NeverEscalates()
    {
        var h = ShadowFailingWith(new TrueLayerException("TRUELAYER_FORBIDDEN", "TrueLayer API error (403)", 403));

        await SyncAtAsync(h, Cycle);

        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.ActionRequired);
        h.Account.SyncStatus.Should().Be("failed", "today a TrueLayer 403 marks the account failed, never reauth_required");
        VerifySyncFailureAlerts(h, Times.Exactly(2));
        h.Alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            UserId, "truelayer", h.Account.Id, h.Account.BankName, "TRUELAYER_FORBIDDEN", SyncFailureClass.Credential,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        VerifyNoConnectionLevelAlert(h);
    }

    [Fact]
    public async Task Shadow_Success_ResolvesOnlyTheAccountAlert()
    {
        var h = ShadowFailingWith(new TrueLayerException(
            "TRUELAYER_BAD_REQUEST", "TrueLayer token endpoint error (400): {\"error\":\"invalid_grant\"}", 400));
        await SyncAsync(h);

        SucceedFromProvider(h);
        h.Time.Advance(Cycle);
        (await SyncAsync(h)).Success.Should().BeTrue();

        h.Alerts.Verify(a => a.ResolveSyncFailureAlertAsync(UserId, "truelayer", h.Account.Id, It.IsAny<CancellationToken>()), Times.Once);
        h.Alerts.Verify(a => a.ResolveSyncFailureAlertAsync(UserId, "truelayer", h.ConnectionId, It.IsAny<CancellationToken>()), Times.Never);
        h.ConnectionHealth.State.Should().Be(ConnectionHealthState.Healthy);
    }

    // ── Typed classification (one test per rule) ────────────────────────────

    private static async Task<(Harness H, Mock<IAlertGeneratorService> Alerts, SyncResult Result)> FailWith(
        Exception ex, string provider = "truelayer")
    {
        var h = FailingWith(ex, provider);
        return (h, h.Alerts, await SyncAsync(h));
    }

    private static void AssertSilentRetry((Harness H, Mock<IAlertGeneratorService> Alerts, SyncResult Result) run)
    {
        run.Result.Success.Should().BeFalse();
        run.H.Account.SyncStatus.Should().Be("active");
        run.H.Account.LastSyncError.Should().BeNull();
        VerifyNoSyncFailureAlert(run.H);
    }

    [Fact]
    public async Task Failure_MonobankHttp500_IsTransient_NoAlert()
        => AssertSilentRetry(await FailWith(new MonobankException(
            "MONOBANK_SERVER_ERROR", "Monobank API error (500): {\"errorDescription\":\"Unknown error\"}", 500), "monobank"));

    [Theory]
    [InlineData(429, "TRUELAYER_RATE_LIMITED")]
    [InlineData(500, "TRUELAYER_ERROR")]
    [InlineData(503, "TRUELAYER_ERROR")]
    public async Task Failure_TrueLayerThrottleOr5xx_IsTransient_NoAlert(int status, string code)
        => AssertSilentRetry(await FailWith(new TrueLayerException(code, $"TrueLayer API error ({status}) on /accounts", status)));

    [Fact]
    public async Task Failure_TrueLayerNetworkError_IsTransient_NoAlert()
        => AssertSilentRetry(await FailWith(new HttpRequestException(
            "Connection refused", new System.Net.Sockets.SocketException())));

    [Fact]
    public async Task Failure_TrueLayerTimeout_IsTransient_NoAlert()
        => AssertSilentRetry(await FailWith(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout")));

    [Fact]
    public async Task Failure_PlaidStyleMessageText_IsNoLongerClassifiedFromTheMessage()
    {
        var run = await FailWith(new HttpRequestException("RATE_LIMIT_EXCEEDED: too many requests"));

        run.Result.ErrorCode.Should().BeNull();
        run.H.Account.SyncStatus.Should().Be("failed");
    }

    [Fact]
    public async Task Failure_TrueLayerBadRequest_LastSyncErrorKeepsOnlyTheErrorCode()
    {
        var run = await FailWith(new TrueLayerException(
            "TRUELAYER_BAD_REQUEST", "TrueLayer API error (400): {\"error\":\"invalid_request\"}", 400));

        run.H.Account.SyncStatus.Should().Be("failed");
        run.H.Account.LastSyncError.Should().Be("TRUELAYER_BAD_REQUEST");
    }

    [Fact]
    public async Task Failure_RecordsTheAccountsOwnStreakToo()
    {
        var run = await FailWith(new TrueLayerException("TRUELAYER_RATE_LIMITED", "TrueLayer API error (429)", 429));

        run.H.Account.Health.State.Should().Be(ConnectionHealthState.Degraded);
        run.H.Account.Health.ConsecutiveFailures.Should().Be(1);
        run.H.AccountRepo.Verify(r => r.SaveHealthUnscopedAsync(
            run.H.Account.Id, It.Is<ConnectionHealth>(health => health.ConsecutiveFailures == 1), It.IsAny<CancellationToken>()),
            Times.Once);
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

    // A consent-expired account (reauth_required) must be skipped by the recurring scheduler so it stops
    // failing every cycle — the loop that logged errorCode=UNKNOWN ~48x/day for an expired TrueLayer consent.
    [Fact]
    public async Task TriggerScheduledSyncAsync_ReauthRequiredAccount_SkipsWithoutSyncing()
    {
        var accountRepo = new Mock<IBankAccountRepository>();
        var syncService = new Mock<IScheduledSyncService>();

        var account = new BankAccount { Provider = "truelayer" };
        account.BeginSync();
        account.MarkReauthRequired();
        accountRepo.Setup(r => r.GetByIdUnscopedAsync(AccountId, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var coordinator = new TransactionSyncCoordinator(
            accountRepo.Object, syncService.Object,
            new Mock<IBackgroundJobClient>().Object, Mock.Of<IBankConnectionHealth>());

        var result = await coordinator.TriggerScheduledSyncAsync(AccountId);

        result.ErrorCode.Should().Be("ITEM_LOGIN_REQUIRED");
        syncService.Verify(
            s => s.PerformFullSyncAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
            Times.Never);
    }

    // The provider asked for a wait (a rate limit's Retry-After): the scheduled run leaves the account alone until
    // then without calling the provider, and goes ahead once the wait is over.
    [Fact]
    public async Task TriggerScheduledSyncAsync_ProviderAskedForAWait_SkipsWithoutSyncing()
    {
        var account = new BankAccount { Provider = "monobank" };
        var accountRepo = new Mock<IBankAccountRepository>();
        accountRepo.Setup(r => r.GetByIdUnscopedAsync(AccountId, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        var syncService = new Mock<IScheduledSyncService>();
        var health = new Mock<IBankConnectionHealth>();
        health
            .Setup(h => h.ScheduledSyncPausedUntilAsync(account, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));
        var coordinator = new TransactionSyncCoordinator(
            accountRepo.Object, syncService.Object, Mock.Of<IBackgroundJobClient>(), health.Object);

        var result = await coordinator.TriggerScheduledSyncAsync(AccountId);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("SYNC_RATE_LIMITED");
        syncService.Verify(
            s => s.PerformFullSyncAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task TriggerScheduledSyncAsync_NoWaitAsked_Syncs()
    {
        var account = new BankAccount { Provider = "monobank" };
        var accountRepo = new Mock<IBankAccountRepository>();
        accountRepo.Setup(r => r.GetByIdUnscopedAsync(AccountId, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        var syncService = new Mock<IScheduledSyncService>();
        syncService
            .Setup(s => s.PerformFullSyncAsync(AccountId, It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(new SyncResult(true, 0, 0, null, null));
        var coordinator = new TransactionSyncCoordinator(
            accountRepo.Object, syncService.Object, Mock.Of<IBackgroundJobClient>(), Mock.Of<IBankConnectionHealth>());

        var result = await coordinator.TriggerScheduledSyncAsync(AccountId);

        result.Success.Should().BeTrue();
        syncService.Verify(
            s => s.PerformFullSyncAsync(AccountId, It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Once);
    }

    // ScheduledSyncService now delegates the refresh-token exchange to ITrueLayerTokenRefreshService
    // (shared with the account-discovery pass, see TrueLayerTokenRefreshServiceTests for the
    // rotation-persistence regression that used to live here) rather than doing it inline.
    [Fact]
    public async Task SyncTrueLayer_AcquiresAccessTokenViaSharedRefreshService()
    {
        var h = BuildSut();
        SetupProviderCandidates(h, []);

        await h.Sut.PerformFullSyncAsync(h.Account.Id);

        h.TrueLayerTokenRefresh.Verify(
            s => s.AcquireAccessTokenAsync(h.Connection.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
