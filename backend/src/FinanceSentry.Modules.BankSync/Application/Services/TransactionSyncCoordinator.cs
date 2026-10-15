namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Modules.BankSync.Application.Connections;
using FinanceSentry.Modules.BankSync.Domain;
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

    /// <summary>
    /// One look at the suspect credential failure of <paramref name="accountId"/>'s connection, scheduled after the
    /// scheduled sync before it (<see cref="IBankConnectionHealth.ScheduleSuspectRecheckIfDue"/>): the account's scheduled
    /// sync again, through the same pauses. <paramref name="recheck"/> is how many looks have run, this one included;
    /// <paramref name="scheduledAt"/>, when it was scheduled. Does nothing once the suspicion is confirmed or cleared, the
    /// connection was tried since (a sibling account's look), or the provider is back in shadow mode.
    /// </summary>
    Task<SyncResult> TriggerSuspectRecheckAsync(
        Guid accountId, int recheck, DateTimeOffset scheduledAt, CancellationToken ct = default);

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

    /// <summary>The result code of a look at a suspect credential failure that was settled before it ran.</summary>
    public const string RecheckNotDueCode = "SYNC_RECHECK_NOT_DUE";

    private readonly IBankAccountRepository _accounts = accounts;
    private readonly IScheduledSyncService _syncService = syncService;
    private readonly IBackgroundJobClient _backgroundJobs = backgroundJobs;
    private readonly IBankConnectionHealth _connectionHealth = connectionHealth;

    /// <inheritdoc />
    public async Task<SyncResult> TriggerScheduledSyncAsync(Guid accountId, CancellationToken ct = default) =>
        await RunScheduledAsync(accountId, await _accounts.GetByIdUnscopedAsync(accountId, ct), recheck: 0, ct);

    /// <inheritdoc />
    public async Task<SyncResult> TriggerSuspectRecheckAsync(
        Guid accountId, int recheck, DateTimeOffset scheduledAt, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdUnscopedAsync(accountId, ct);
        if (account is null
            || await _connectionHealth.ReadConnectionHealthAsync(account, ct) is not { } health
            || !_connectionHealth.IsSuspectRecheckDue(account, health, scheduledAt))
            return new SyncResult(false, 0, 0, RecheckNotDueCode, "The connection's suspect credential failure is settled; no look made.");

        return await RunScheduledAsync(accountId, account, recheck, ct);
    }

    private async Task<SyncResult> RunScheduledAsync(Guid accountId, BankAccount? account, int recheck, CancellationToken ct)
    {
        // An account whose provider consent has expired/been revoked cannot sync until the user
        // reconnects. Skip it in the recurring scheduler so it stops failing every cycle; the reconnect
        // flow clears the state via MarkActive. Manual syncs are unaffected.
        if (account?.SyncStatus == "reauth_required")
            return new SyncResult(false, 0, 0, "ITEM_LOGIN_REQUIRED", "Account requires reconnection; scheduled sync skipped.");

        if (account is null)
            return ChaseWithDetection(await _syncService.PerformFullSyncAsync(accountId, ct: ct));

        // The provider asked for a wait (a rate limit's Retry-After): the scheduled run leaves it alone until then.
        if (await _connectionHealth.ScheduledSyncPausedUntilAsync(account, ct) is { } pausedUntil)
            return new SyncResult(false, 0, 0, "SYNC_RATE_LIMITED", $"The provider asked for a wait until {pausedUntil:O}; scheduled sync skipped.");

        // A Dormant account or connection of an active provider syncs once a day.
        if (await _connectionHealth.DormantSyncPausedUntilAsync(account, ct) is { } dormantUntil)
            return new SyncResult(false, 0, 0, DormantCode, $"The connection is Dormant; its next scheduled sync is at {dormantUntil:O}.");

        var before = await _connectionHealth.ReadConnectionHealthAsync(account, ct);
        var result = ChaseWithDetection(await _syncService.PerformFullSyncAsync(accountId, ct: ct));

        // A failure that raised a suspect credential signal (or a look that left it standing) is looked at again.
        if (!result.Success && before is not null && await _connectionHealth.ReadConnectionHealthAsync(account, ct) is { } after)
        {
            _connectionHealth.ScheduleSuspectRecheckIfDue(
                account,
                before,
                after,
                recheck,
                (delay, at) => _backgroundJobs.Schedule<ScheduledSyncJob>(
                    job => job.ExecuteSuspectRecheckAsync(accountId, recheck + 1, at), delay));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<SyncResult> TriggerManualSyncAsync(Guid accountId, CancellationToken ct = default)
    {
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
