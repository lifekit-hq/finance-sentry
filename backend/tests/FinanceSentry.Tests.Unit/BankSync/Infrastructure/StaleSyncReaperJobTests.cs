namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="StaleSyncReaperJob"/> (TrueLayer #3). Verifies the reaper releases
/// syncs orphaned by a crash/restart — the deadlock that froze Revolut for 5 days — while leaving
/// genuinely in-progress syncs alone on the periodic cadence.
/// </summary>
public class StaleSyncReaperJobTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private readonly Mock<ISyncJobRepository> _jobs = new();
    private readonly Mock<IBankAccountRepository> _accounts = new();

    public StaleSyncReaperJobTests()
    {
        _jobs.Setup(r => r.GetByStatusUnscopedAsync("running", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SyncJob>());
        _jobs.Setup(r => r.GetByStatusUnscopedAsync("pending", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SyncJob>());
        _accounts.Setup(r => r.GetBySyncStatusUnscopedAsync("syncing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BankAccount>());
    }

    [Fact]
    public async Task Startup_ReapsRunningJobAndSyncingAccount_RegardlessOfAge()
    {
        var job = new SyncJob(Guid.NewGuid(), UserId); // CreatedAt == now (fresh)
        var account = MakeSyncingAccount();
        _jobs.Setup(r => r.GetByStatusUnscopedAsync("running", It.IsAny<CancellationToken>()))
            .ReturnsAsync([job]);
        _accounts.Setup(r => r.GetBySyncStatusUnscopedAsync("syncing", It.IsAny<CancellationToken>()))
            .ReturnsAsync([account]);

        await MakeReaper().ExecuteAsync(startupSweep: true);

        job.Status.Should().Be("failed");
        job.ErrorCode.Should().Be("STALE_JOB_REAPED");
        account.SyncStatus.Should().Be("active");
        account.LastSyncError.Should().BeNull("a reaped sync is not a provider failure");
        _jobs.Verify(r => r.UpdateAsync(job, It.IsAny<CancellationToken>()), Times.Once);
        _accounts.Verify(r => r.UpdateAsync(account, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Periodic_LeavesFreshInFlightSyncAlone()
    {
        var job = new SyncJob(Guid.NewGuid(), UserId); // fresh — a real sync in progress
        var account = MakeSyncingAccount();            // UpdatedAt == now
        _jobs.Setup(r => r.GetByStatusUnscopedAsync("running", It.IsAny<CancellationToken>()))
            .ReturnsAsync([job]);
        _accounts.Setup(r => r.GetBySyncStatusUnscopedAsync("syncing", It.IsAny<CancellationToken>()))
            .ReturnsAsync([account]);

        await MakeReaper().ExecuteAsync(startupSweep: false);

        job.Status.Should().Be("pending");   // untouched (SyncJob default status)
        account.SyncStatus.Should().Be("syncing");
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()), Times.Never);
        _accounts.Verify(r => r.UpdateAsync(It.IsAny<BankAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Periodic_ReapsSyncOlderThanThreshold()
    {
        var staleMinutes = StaleSyncReaperJob.StaleThresholdMinutes + 5;
        var job = Backdate(new SyncJob(Guid.NewGuid(), UserId), staleMinutes);
        var account = Backdate(MakeSyncingAccount(), staleMinutes);
        _jobs.Setup(r => r.GetByStatusUnscopedAsync("running", It.IsAny<CancellationToken>()))
            .ReturnsAsync([job]);
        _accounts.Setup(r => r.GetBySyncStatusUnscopedAsync("syncing", It.IsAny<CancellationToken>()))
            .ReturnsAsync([account]);

        await MakeReaper().ExecuteAsync(startupSweep: false);

        job.Status.Should().Be("failed");
        account.SyncStatus.Should().Be("active");
        account.LastSyncError.Should().BeNull("a reaped sync is not a provider failure");
        _jobs.Verify(r => r.UpdateAsync(job, It.IsAny<CancellationToken>()), Times.Once);
        _accounts.Verify(r => r.UpdateAsync(account, It.IsAny<CancellationToken>()), Times.Once);
    }

    private StaleSyncReaperJob MakeReaper()
        => new(_jobs.Object, _accounts.Object, Mock.Of<ILogger<StaleSyncReaperJob>>());

    private static BankAccount MakeSyncingAccount()
    {
        var account = new BankAccount(UserId, "item_abc123", "REVOLUT-IE", "checking",
            "1234", "John Doe", "EUR", UserId, "truelayer");
        account.BeginSync();
        return account;
    }

    // CreatedAt (init) and UpdatedAt (protected set) are only assignable via reflection in tests —
    // used here to age an entity past the stale threshold without waiting.
    private static T Backdate<T>(T entity, int minutesAgo) where T : Entity
    {
        var when = DateTime.UtcNow.AddMinutes(-minutesAgo);
        typeof(Entity).GetProperty(nameof(Entity.CreatedAt))!.SetValue(entity, when);
        typeof(Entity).GetProperty(nameof(Entity.UpdatedAt))!.SetValue(entity, when);
        return entity;
    }
}
