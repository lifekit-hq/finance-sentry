namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using Hangfire;
using FinanceSentry.Modules.BankSync.Application.Services;

/// <summary>
/// Hangfire job that triggers a scheduled (non-webhook) transaction sync for one account.
/// AutomaticRetry is disabled at the Hangfire level and there is no inline retry:
/// a failed run is retried by the next scheduled cycle (see <c>HangfireSetup.PerAccountCron</c>).
/// </summary>
public class ScheduledSyncJob(ITransactionSyncCoordinator coordinator)
{
    private readonly ITransactionSyncCoordinator _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

    /// <summary>
    /// Entry-point called by Hangfire. Delegates to the coordinator which enforces
    /// the "no concurrent sync" invariant before handing off to the sync service.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteSyncAsync(Guid accountId)
          => await _coordinator.TriggerScheduledSyncAsync(accountId);

    /// <summary>
    /// Entry-point for one look at a suspect credential failure on the account's connection, scheduled after the
    /// scheduled sync before it; see <see cref="ITransactionSyncCoordinator.TriggerSuspectRecheckAsync"/>.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteSuspectRecheckAsync(Guid accountId, int recheck, DateTimeOffset scheduledAt)
          => await _coordinator.TriggerSuspectRecheckAsync(accountId, recheck, scheduledAt);

    /// <summary>
    /// Entry-point for a sync a person asked for (<c>POST /accounts/{id}/sync</c>). It goes through the
    /// coordinator's manual path, so a provider-named wait (<c>NotBefore</c>) that pauses the scheduled run
    /// does not silently drop it.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteManualSyncAsync(Guid accountId)
          => await _coordinator.TriggerManualSyncAsync(accountId);
}
