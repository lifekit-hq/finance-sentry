namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using Microsoft.Extensions.Logging;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// Crash recovery for the sync claim. A sync holds its account by moving it to "syncing"
/// (<see cref="IBankAccountRepository.TryClaimSyncUnscopedAsync"/>); a process kill (deploy/restart) or a crash
/// between the claim and completion leaves the account "syncing" and its <see cref="Domain.SyncJob"/>
/// "running", and the claim would refuse every later sync for that account.
///
/// No sync survives a process restart, so a sweep enqueued at startup (it runs on a Hangfire worker) fails
/// every in-flight job and releases every "syncing" account.
/// </summary>
public class StaleSyncReaperJob(
    ISyncJobRepository syncJobs,
    IBankAccountRepository accounts,
    ILogger<StaleSyncReaperJob> logger)
{
    private const string ReapedErrorCode = "STALE_JOB_REAPED";

    private readonly ISyncJobRepository _syncJobs = syncJobs;
    private readonly IBankAccountRepository _accounts = accounts;
    private readonly ILogger<StaleSyncReaperJob> _logger = logger;

    /// <summary>Startup entry-point (Hangfire): releases every sync the previous process left in flight.</summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var reapedJobs = 0;
        foreach (var job in await _syncJobs.GetByStatusUnscopedAsync("running", cancellationToken))
        {
            job.MarkFailed("Sync did not complete and was reaped as stale.", ReapedErrorCode);
            await _syncJobs.UpdateAsync(job, cancellationToken);
            reapedJobs++;
        }

        // An orphaned sync says nothing about the provider, so the account goes back to "active"
        // with no error (MarkTransientRetry) instead of "failed", which would read as a provider failure.
        var resetAccounts = 0;
        foreach (var account in await _accounts.GetBySyncStatusUnscopedAsync("syncing", cancellationToken))
        {
            account.MarkTransientRetry();
            await _accounts.UpdateAsync(account, cancellationToken);
            resetAccounts++;
        }

        if (reapedJobs > 0 || resetAccounts > 0)
            _logger.LogWarning(
                "Stale-sync reaper reaped {Jobs} dangling job(s) and reset {Accounts} wedged account(s).",
                reapedJobs, resetAccounts);
    }
}
