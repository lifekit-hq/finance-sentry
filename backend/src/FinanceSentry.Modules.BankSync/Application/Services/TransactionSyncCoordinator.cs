namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using Hangfire;

/// <summary>
/// Coordinates sync requests from multiple trigger sources (scheduler, manual).
/// Ensures only one sync runs at a time per account — additional requests are silently dropped.
/// </summary>
public interface ITransactionSyncCoordinator
{
    /// <summary>Trigger a sync initiated by the recurring background scheduler.</summary>
    Task<SyncResult> TriggerScheduledSyncAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Trigger a sync initiated manually by the user via the API.</summary>
    Task<SyncResult> TriggerManualSyncAsync(Guid accountId, CancellationToken ct = default);
}

/// <inheritdoc />
public class TransactionSyncCoordinator(
    ISyncJobRepository syncJobs,
    IBankAccountRepository accounts,
    IScheduledSyncService syncService,
    IBackgroundJobClient backgroundJobs) : ITransactionSyncCoordinator
{
    private readonly ISyncJobRepository _syncJobs = syncJobs;
    private readonly IBankAccountRepository _accounts = accounts;
    private readonly IScheduledSyncService _syncService = syncService;
    private readonly IBackgroundJobClient _backgroundJobs = backgroundJobs;

    /// <inheritdoc />
    public async Task<SyncResult> TriggerScheduledSyncAsync(Guid accountId, CancellationToken ct = default)
    {
        if (await _syncJobs.HasRunningJobUnscopedAsync(accountId, ct))
            return new SyncResult(false, 0, 0, "SYNC_IN_PROGRESS", "A sync is already in progress for this account.");

        // An account whose provider consent has expired/been revoked cannot sync until the user
        // reconnects. Skip it in the recurring scheduler so it stops failing every cycle; the reconnect
        // flow clears the state via MarkActive. Manual syncs are unaffected.
        var account = await _accounts.GetByIdUnscopedAsync(accountId, ct);
        if (account?.SyncStatus == "reauth_required")
            return new SyncResult(false, 0, 0, "ITEM_LOGIN_REQUIRED", "Account requires reconnection; scheduled sync skipped.");

        return ChaseWithDetection(await _syncService.PerformFullSyncAsync(accountId, ct: ct));
    }

    /// <inheritdoc />
    public async Task<SyncResult> TriggerManualSyncAsync(Guid accountId, CancellationToken ct = default)
    {
        if (await _syncJobs.HasRunningJobUnscopedAsync(accountId, ct))
            return new SyncResult(false, 0, 0, "SYNC_IN_PROGRESS", "A sync is already in progress for this account.");

        return ChaseWithDetection(await _syncService.PerformFullSyncAsync(accountId, ct: ct));
    }

    /// <summary>
    /// A sync that landed new transactions can change the subscription/installment picture
    /// (a new розстрочка, a fresh charge) — refresh detection right away instead of waiting
    /// for the daily recurring job. The detection job is cheap and idempotent, so enqueueing
    /// once per productive sync is fine.
    /// </summary>
    private SyncResult ChaseWithDetection(SyncResult result)
    {
        if (result.Success && result.TransactionCountDeduped > 0)
            _backgroundJobs.Enqueue<SubscriptionDetectionJob>(job => job.ExecuteAsync(CancellationToken.None));

        return result;
    }
}
