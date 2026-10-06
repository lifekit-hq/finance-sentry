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
}
