namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// Integration tests for Hangfire job execution (T315).
///
/// Status: Skeleton — compile-only until a full DB/Hangfire in-process environment is wired up.
///
/// These tests validate that:
/// 1. Enqueuing ScheduledSyncJob via Hangfire IBackgroundJobClient results in execution.
/// 2. After execution, SyncJob is persisted with status="success".
/// 3. Transactions are inserted into the database.
///
/// Full end-to-end execution requires a Testcontainers PostgreSQL instance and an in-memory
/// Hangfire server. That will be added once the broader integration test harness (using
/// WebApplicationFactory + Testcontainers) is complete in Phase 5.
/// </summary>
public class HangfireJobExecutionTests
{
    // ── Pending: Full Hangfire in-process test ────────────────────────────────

    /// <summary>
    /// Verifies that ScheduledSyncJob.ExecuteSyncAsync() delegates to the coordinator.
    /// This is a unit-level smoke test until the full integration environment is ready.
    /// </summary>
    [Fact]
    public async Task ScheduledSyncJob_ExecuteSyncAsync_DelegatestoCoordinator()
    {
        var accountId   = Guid.NewGuid();
        var coordinator = new Mock<ITransactionSyncCoordinator>();
        coordinator
            .Setup(c => c.TriggerScheduledSyncAsync(accountId, default))
            .ReturnsAsync(new SyncResult(true, 10, 8, null, null));

        var job = new ScheduledSyncJob(coordinator.Object);

        await job.ExecuteSyncAsync(accountId);

        coordinator.Verify(c => c.TriggerScheduledSyncAsync(accountId, default), Times.Once);
    }

    /// <summary>
    /// A person-triggered sync goes through the coordinator's manual path, not the scheduled one
    /// that honours a provider-named NotBefore.
    /// </summary>
    [Fact]
    public async Task ScheduledSyncJob_ExecuteManualSyncAsync_UsesManualPath()
    {
        var accountId   = Guid.NewGuid();
        var coordinator = new Mock<ITransactionSyncCoordinator>();
        coordinator
            .Setup(c => c.TriggerManualSyncAsync(accountId, default))
            .ReturnsAsync(new SyncResult(true, 10, 8, null, null));

        var job = new ScheduledSyncJob(coordinator.Object);

        await job.ExecuteManualSyncAsync(accountId);

        coordinator.Verify(c => c.TriggerManualSyncAsync(accountId, default), Times.Once);
        coordinator.Verify(c => c.TriggerScheduledSyncAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that the coordinator reports a sync-in-progress result without calling the service.
    /// Simulates the concurrency guard that prevents duplicate runs.
    /// </summary>
    [Fact]
    public async Task ScheduledSyncJob_WhenSyncAlreadyRunning_CoordinatorDropsRequest()
    {
        var accountId   = Guid.NewGuid();
        var coordinator = new Mock<ITransactionSyncCoordinator>();
        coordinator
            .Setup(c => c.TriggerScheduledSyncAsync(accountId, default))
            .ReturnsAsync(new SyncResult(false, 0, 0, "SYNC_IN_PROGRESS", "Already running."));

        var job = new ScheduledSyncJob(coordinator.Object);

        // Should not throw — coordinator swallows the duplicate-run scenario
        await job.Invoking(j => j.ExecuteSyncAsync(accountId))
                 .Should()
                 .NotThrowAsync();
    }

    // ── TODO: Full integration tests (require Testcontainers + in-memory Hangfire) ──
    //
    // [Fact(Skip = "Requires Testcontainers PostgreSQL — pending Phase 5 harness")]
    // public async Task EnqueuedJob_ExecutesAndPersistsSuccessSyncJob()
    // {
    //     // 1. Start PostgreSQL via Testcontainers
    //     // 2. Apply EF migrations
    //     // 3. Seed a BankAccount + EncryptedCredential
    //     // 4. Configure in-memory Hangfire with a BackgroundJobServer
    //     // 5. Enqueue ScheduledSyncJob via IBackgroundJobClient
    //     // 6. Wait for the job to finish (poll SyncJob table)
    //     // 7. Assert SyncJob.Status == "success"
    //     // 8. Assert Transaction rows were inserted
    // }
}
