namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Modules.BankSync.Application.Connections;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using Hangfire;

/// <summary>
/// Coordinates sync requests from multiple trigger sources (scheduler, manual).
/// Ensures only one sync runs at a time per account — <see cref="IScheduledSyncService"/> claims the account atomically and refuses a second start.
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
    IBankAccountRepository accounts,
    IScheduledSyncService syncService,
    IBackgroundJobClient backgroundJobs,
    IBankConnectionHealth connectionHealth) : ITransactionSyncCoordinator
{
    /// <summary>The result code of a scheduled sync the Dormant cadence held back.</summary>
    public const string DormantCode = "SYNC_DORMANT";

    /// <summary>The result code of a sync of a retired account: nothing syncs it until it is reopened.</summary>
    public const string ClosedCode = "ACCOUNT_CLOSED";

    private static readonly SyncResult ClosedResult =
        new(false, 0, 0, ClosedCode, "The account is retired; reopen it to sync again.");

    private readonly IBankAccountRepository _accounts = accounts;
    private readonly IScheduledSyncService _syncService = syncService;
    private readonly IBackgroundJobClient _backgroundJobs = backgroundJobs;

    /// <inheritdoc />
    public async Task<SyncResult> TriggerScheduledSyncAsync(Guid accountId, CancellationToken ct = default)
    {
        // An account whose provider consent has expired/been revoked cannot sync until the user
        // reconnects. Skip it in the recurring scheduler so it stops failing every cycle; the reconnect
        // flow clears the state via MarkActive. Manual syncs are unaffected.
        var account = await _accounts.GetByIdUnscopedAsync(accountId, ct);
        if (account?.IsClosed == true)
            return ClosedResult;

        if (account?.SyncStatus == "reauth_required")
            return new SyncResult(false, 0, 0, "ITEM_LOGIN_REQUIRED", "Account requires reconnection; scheduled sync skipped.");

        // The provider asked for a wait (a rate limit's Retry-After): the scheduled run leaves it alone until then.
        if (account is not null && await connectionHealth.ScheduledSyncPausedUntilAsync(account, ct) is { } pausedUntil)
            return new SyncResult(false, 0, 0, "SYNC_RATE_LIMITED", $"The provider asked for a wait until {pausedUntil:O}; scheduled sync skipped.");

        // A Dormant account or connection of an active provider syncs once a day.
        if (account is not null && await connectionHealth.DormantSyncPausedUntilAsync(account, ct) is { } dormantUntil)
            return new SyncResult(false, 0, 0, DormantCode, $"The connection is Dormant; its next scheduled sync is at {dormantUntil:O}.");

        return ChaseWithDetection(await _syncService.PerformFullSyncAsync(accountId, ct: ct));
    }

    /// <inheritdoc />
    public async Task<SyncResult> TriggerManualSyncAsync(Guid accountId, CancellationToken ct = default)
    {
        // A retired account is not synced on request either: the provider no longer serves it, and a failed run would
        // raise the alerts the retirement resolved.
        if ((await _accounts.GetByIdUnscopedAsync(accountId, ct))?.IsClosed == true)
            return ClosedResult;

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
