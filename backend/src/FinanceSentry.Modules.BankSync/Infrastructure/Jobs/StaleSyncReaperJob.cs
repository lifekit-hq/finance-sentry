namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using Hangfire;
using Microsoft.Extensions.Logging;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// Reaps sync operations orphaned by a process kill (deploy/restart) or a crash between
/// <c>BeginSync</c> and completion. Such an interruption leaves a <see cref="Domain.SyncJob"/>
/// stuck in "running"/"pending" and its account stuck in "syncing"; because the coordinator refuses
/// to start a new sync while a "running" job exists (<c>HasRunningJobAsync</c>), the account would
/// otherwise never sync again — a permanent deadlock.
///
/// On startup every in-flight job is stale by definition (no sync survives a process restart), so
/// all are reaped. On the recurring cadence only jobs/accounts older than
/// <see cref="StaleThresholdMinutes"/> are reaped, leaving genuinely in-progress syncs alone.
/// </summary>
public class StaleSyncReaperJob(
    ISyncJobRepository syncJobs,
    IBankAccountRepository accounts,
    ILogger<StaleSyncReaperJob> logger)
{
    /// <summary>
    /// Minutes after which an unfinished sync is considered dead. No legitimate sync runs this
    /// long, so anything older was orphaned by a crash/restart.
    /// </summary>
    public const int StaleThresholdMinutes = 30;

    private const string ReapedErrorCode = "STALE_JOB_REAPED";

    private readonly ISyncJobRepository _syncJobs = syncJobs;
    private readonly IBankAccountRepository _accounts = accounts;
    private readonly ILogger<StaleSyncReaperJob> _logger = logger;

    /// <summary>Recurring entry-point (Hangfire): reaps only jobs/accounts past the stale threshold.</summary>
    [AutomaticRetry(Attempts = 0)]
    public Task ReapAsync() => ExecuteAsync(startupSweep: false, CancellationToken.None);

    /// <summary>
    /// Core reap. When <paramref name="startupSweep"/> is true, every in-flight job / syncing
    /// account is reaped regardless of age (nothing can still be running after a restart).
    /// </summary>
    public async Task ExecuteAsync(bool startupSweep, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-StaleThresholdMinutes);
        var reapedJobs = 0;
        var resetAccounts = 0;

        // 1. Fail sync jobs left dangling by a crash/restart so HasRunningJobAsync stops blocking.
        var inFlight = (await _syncJobs.GetByStatusUnscopedAsync("running", cancellationToken))
            .Concat(await _syncJobs.GetByStatusUnscopedAsync("pending", cancellationToken));
        foreach (var job in inFlight)
        {
            if (!startupSweep && job.CreatedAt > cutoff)
                continue;

            job.MarkFailed("Sync did not complete and was reaped as stale.", ReapedErrorCode);
            await _syncJobs.UpdateAsync(job, cancellationToken);
            reapedJobs++;
        }

        // 2. Release accounts wedged in "syncing" so the next scheduled cycle can re-run them.
        //    An orphaned sync says nothing about the provider, so the account goes back to "active"
        //    with no error (MarkTransientRetry) instead of "failed", which would read as a provider failure.
        var stuck = await _accounts.GetBySyncStatusUnscopedAsync("syncing", cancellationToken);
        foreach (var account in stuck)
        {
            if (!startupSweep && account.UpdatedAt > cutoff)
                continue;

            account.MarkTransientRetry();
            await _accounts.UpdateAsync(account, cancellationToken);
            resetAccounts++;
        }

        if (reapedJobs > 0 || resetAccounts > 0)
            _logger.LogWarning(
                "Stale-sync reaper ({Mode}) reaped {Jobs} dangling job(s) and reset {Accounts} wedged account(s).",
                startupSweep ? "startup" : "periodic", reapedJobs, resetAccounts);
    }
}
