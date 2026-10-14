using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.BankSync.Jobs;

/// <summary>Startup/periodic reconciliation: a recurring per-account sync job lives only as long as its account is active.</summary>
public class SyncSchedulerReconciliationTests
{
    private readonly Mock<IBankAccountRepository> _accounts = new();
    private readonly Mock<IRecurringJobManager> _recurringJobs = new();
    private readonly Mock<IAccountDiscoveryService> _discovery = new();
    private readonly Mock<IClosedAccountsReader> _closed = new();
    private readonly InMemoryStorage _storage = new();

    public SyncSchedulerReconciliationTests()
        => _closed.Setup(c => c.GetClosedUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<Guid>());

    private SyncScheduler Scheduler() =>
        new(_accounts.Object, _recurringJobs.Object, _storage, _discovery.Object, _closed.Object, NullLogger<SyncScheduler>.Instance);

    private void Active(params BankAccount[] active)
        => _accounts.Setup(a => a.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);

    // The mocked manager writes nothing, so the stored recurring jobs are seeded straight through a real manager.
    private void StoredRecurringJob(string id)
        => new RecurringJobManager(_storage).AddOrUpdate(id, Hangfire.Common.Job.FromExpression(() => Console.WriteLine()), Cron.Daily());

    [Fact]
    public async Task A_job_whose_account_is_no_longer_active_is_removed()
    {
        var gone = Guid.NewGuid();
        var kept = new BankAccount();
        StoredRecurringJob(SyncAccountJob.IdFor(gone));
        StoredRecurringJob(SyncAccountJob.IdFor(kept.Id));
        Active(kept);

        await Scheduler().ScheduleAllActiveAccounts();

        _recurringJobs.Verify(m => m.RemoveIfExists(SyncAccountJob.IdFor(gone)), Times.Once);
        _recurringJobs.Verify(m => m.RemoveIfExists(SyncAccountJob.IdFor(kept.Id)), Times.Never);
    }

    [Fact]
    public async Task Jobs_that_are_not_per_account_syncs_are_left_alone()
    {
        StoredRecurringJob("bank-account-sync-scheduler");
        StoredRecurringJob("sync-account-not-a-guid-but-stale");
        Active();

        await Scheduler().ScheduleAllActiveAccounts();

        _recurringJobs.Verify(m => m.RemoveIfExists("bank-account-sync-scheduler"), Times.Never);
        _recurringJobs.Verify(m => m.RemoveIfExists("sync-account-not-a-guid-but-stale"), Times.Once);
    }

    [Fact]
    public async Task Nothing_is_removed_when_every_stored_job_has_an_active_account()
    {
        var account = new BankAccount();
        StoredRecurringJob(SyncAccountJob.IdFor(account.Id));
        Active(account);

        await Scheduler().ScheduleAllActiveAccounts();

        _recurringJobs.Verify(m => m.RemoveIfExists(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task A_closed_accounts_jobs_are_removed_and_not_rescheduled()
    {
        var closedOwner = Guid.NewGuid();
        var closed = new BankAccount { UserId = closedOwner };
        var open = new BankAccount { UserId = Guid.NewGuid() };
        StoredRecurringJob(SyncAccountJob.IdFor(closed.Id));
        StoredRecurringJob(SyncAccountJob.IdFor(open.Id));
        Active(closed, open);
        _closed.Setup(c => c.GetClosedUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<Guid> { closedOwner });

        await Scheduler().ScheduleAllActiveAccounts();

        _recurringJobs.Verify(m => m.RemoveIfExists(SyncAccountJob.IdFor(closed.Id)), Times.Once);
        _recurringJobs.Verify(m => m.RemoveIfExists(SyncAccountJob.IdFor(open.Id)), Times.Never);
    }
}
