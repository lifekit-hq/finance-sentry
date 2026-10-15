using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Connections;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;

/// <summary>
/// Daily recurring pull of recent IBKR trade/cash-transaction activity via the Flex Web
/// Service (fs-435 S5). Pulls a short trailing window rather than the saved query's full
/// default range, since re-pulling an overlapping window is a cheap no-op (idempotent on
/// execution id / cash transaction hash) — the one-shot <see cref="IbkrFlexBackfillJob"/>
/// covers full history. A clean no-op, logged and returned from, when no user has an active
/// Flex credential — supplying one is a manual step in IBKR's account management.
///
/// The window ends yesterday, never today: IBKR answers a window ending on a day it has not closed
/// yet with "statement is not available" (1003), which is what failed every weekday run while the
/// window ended today. Should yesterday not be published yet either, the run asks once more through
/// the day before; the overlapping window picks the missing day up on the next run.
///
/// Each attempt is recorded with the connection-health policy (Option B), read by
/// <see cref="IbkrFlexFailureClassifier"/>. With <c>ibkr-flex</c> in
/// <see cref="ConnectionHealthOptions.ActiveProviders"/> the policy's verdict raises and resolves the in-app
/// sync-failure alert (a rejected or expired token at once, anything else once it has persisted: by default
/// 3 failures and 2 h without a success, three daily runs); otherwise (shadow mode) this job raises none,
/// as before the policy existed. A "statement is not available" answer that survives the step-back is
/// recorded as not ready: the policy does not count it while the last success (the credential's creation, before
/// the first one) is younger than the source's
/// <see cref="ConnectionHealthSourceOptions.NotReadyDeadline"/> (48 h by default), and the job asks again sooner
/// than the next daily run (<see cref="ConnectionHealthSourceOptions.NotReadyRetryAfter"/>, 2 h by default),
/// through <see cref="RetryUserAsync"/>, until the next daily run is due.
///
/// A counted failure of a transient kind (transient, unclassified, or a suspect credential signal) on the daily
/// run schedules an earlier re-check through <see cref="RecheckUserAsync"/>, one per entry of
/// <see cref="ConnectionHealthSourceOptions.EarlyRechecks"/> (1 h, then 4 h after that, by default), so a real
/// outage reaches Failing within hours rather than on the third day. The chain ends with the list, and a re-check
/// never starts another chain beside it. It leaves a rate-limited answer to <see cref="ConnectionHealth.NotBefore"/>,
/// a not-ready one to the retry above, and a confirmed credential loss to the pause; a re-check goes through the same
/// gate, so it also waits out NotBefore and a pause, and it does nothing once the connection is healthy again.
/// </summary>
public sealed class IbkrFlexIncrementalSyncJob(
    IIBKRFlexCredentialRepository credentialRepository,
    IIbkrFlexTradeSyncService syncService,
    IAlertGeneratorService alerts,
    IUserAlertPreferencesReader userPreferences,
    IConnectionHealthShadow connectionHealth,
    IbkrFlexFailureClassifier failureClassifier,
    IOptionsMonitor<ConnectionHealthOptions> healthOptions,
    IOptionsMonitor<ConnectionHealthSourceOptions> sourceOptions,
    IBackgroundJobClient backgroundJobs,
    TimeProvider time,
    ILogger<IbkrFlexIncrementalSyncJob> logger)
{
    private const int LookbackDays = 7;

    // How many days further back than yesterday the window's end may move when IBKR has no statement yet.
    private const int MaxEndDateStepBacks = 1;
    private const string Provider = IbkrFlexFailureClassifier.ProviderName;

    // The recurring run is daily: a chain of sooner retries hands over to it rather than overlap it.
    private static readonly TimeSpan RecurringInterval = TimeSpan.FromDays(1);

    public async Task ExecuteAsync()
    {
        var activeCredentials = await credentialRepository.GetAllActiveUnscopedAsync();
        if (activeCredentials.Count == 0)
        {
            logger.LogInformation("No active IBKR Flex credentials; skipping incremental trade sync.");
            return;
        }

        var policyDrivesAlerts = healthOptions.CurrentValue.IsActive(Provider);
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

        foreach (var credential in activeCredentials)
        {
            if (ConnectionHealthScheduledRunGate.ShouldSkip(
                    logger, Provider, credential.UserId, credential.Health, time.GetUtcNow(), healthOptions.CurrentValue))
                continue;

            await SyncCredentialAsync(credential, today, policyDrivesAlerts, attempt: 0, recheck: 0);
        }
    }

    /// <summary>
    /// One sooner retry of a user's sync after a "not ready" answer, scheduled by the run before it. Goes through the
    /// same gate and recording as the daily run, and hands over to that run once its attempts are spent.
    /// </summary>
    public async Task RetryUserAsync(Guid userId, int attempt)
    {
        var credential = await credentialRepository.GetByUserIdUnscopedAsync(userId);
        if (credential is not { IsActive: true })
            return;

        if (ConnectionHealthScheduledRunGate.ShouldSkip(
                logger, Provider, credential.UserId, credential.Health, time.GetUtcNow(), healthOptions.CurrentValue))
            return;

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        await SyncCredentialAsync(credential, today, healthOptions.CurrentValue.IsActive(Provider), attempt, recheck: null);
    }

    /// <summary>
    /// One earlier re-check of a user's sync after a counted failure, scheduled by the run before it (the daily run,
    /// or the re-check before this one). <paramref name="recheck"/> is how many re-checks have run, this one included.
    /// Goes through the same gate and recording as the daily run; skipped when the connection has recovered since.
    /// </summary>
    public async Task RecheckUserAsync(Guid userId, int recheck)
    {
        var credential = await credentialRepository.GetByUserIdUnscopedAsync(userId);
        if (credential is not { IsActive: true } || credential.Health.ConsecutiveFailures == 0)
            return;

        if (ConnectionHealthScheduledRunGate.ShouldSkip(
                logger, Provider, credential.UserId, credential.Health, time.GetUtcNow(), healthOptions.CurrentValue))
            return;

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        await SyncCredentialAsync(credential, today, healthOptions.CurrentValue.IsActive(Provider), attempt: 0, recheck);
    }

    private async Task SyncCredentialAsync(IBKRFlexCredential credential, DateOnly today, bool policyDrivesAlerts, int attempt, int? recheck)
    {
        try
        {
            await SyncThroughLastClosedDayAsync(credential.UserId, today);
            var evaluation = await connectionHealth.RecordSuccessAsync(
                HealthSubject(credential), credential.Health, (health, ct) => SaveHealthAsync(credential, health, ct));
            if (policyDrivesAlerts)
            {
                await ConnectionHealthSyncFailureAlerts.ResolveIfRecoveredAsync(
                    alerts, userPreferences, logger, credential.UserId, Provider, evaluation);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to sync IBKR Flex trades for user {UserId} (Flex error code {FlexErrorCode})",
                credential.UserId,
                (ex as IbkrFlexException)?.FlexErrorCode);
            await RecordFailureAsync(credential, ex);
            var failure = failureClassifier.Classify(ex, new ProviderFailureContext(time.GetUtcNow(), credential.Health));
            var evaluation = await connectionHealth.RecordFailureAsync(
                HealthSubject(credential),
                credential.Health,
                failure,
                (health, ct) => SaveHealthAsync(credential, health, ct));
            if (policyDrivesAlerts)
            {
                await ConnectionHealthSyncFailureAlerts.RaiseIfDueAsync(
                    alerts, userPreferences, logger, credential.UserId, Provider, evaluation, ex.GetType().Name);
            }

            ScheduleNotReadyRetry(credential.UserId, failure, attempt);
            ScheduleEarlyRecheck(credential.UserId, failure, evaluation, recheck);
        }
    }

    // After a failure that counted and may be over by the time we look again: ask again at the source's next early
    // re-check interval, never sooner than the provider's NotBefore. `recheck` is null on a run that must not start
    // a chain (the not-ready retries have their own); the list's length is the bound, so the chain cannot loop.
    // Inzhur is not on the policy yet: when it joins, it takes the same hook from its sync job.
    private void ScheduleEarlyRecheck(Guid userId, ProviderFailure failure, ConnectionHealthEvaluation? evaluation, int? recheck)
    {
        var intervals = sourceOptions.Get(Provider).EarlyRechecks;
        if (recheck is not { } done
            || done >= intervals.Length
            || evaluation is null
            || !IsRecheckable(failure)
            || evaluation.Health.ConsecutiveFailures <= evaluation.Previous.ConsecutiveFailures
            || evaluation.Health.State is ConnectionHealthState.ActionRequired or ConnectionHealthState.Dormant
            || intervals[done] <= TimeSpan.Zero)
            return;

        var now = time.GetUtcNow();
        var delay = intervals[done];
        if (evaluation.Health.NotBefore is { } notBefore && notBefore - now > delay)
            delay = notBefore - now;

        try
        {
            backgroundJobs.Schedule<IbkrFlexIncrementalSyncJob>(job => job.RecheckUserAsync(userId, done + 1), delay);
            logger.LogInformation(
                "IBKR Flex sync failed for user {UserId} ({FailureClass}); checking again in {Delay} (re-check {Recheck} of {Total})",
                userId,
                failure.Class,
                delay,
                done + 1,
                intervals.Length);
        }
        catch (Exception scheduleEx)
        {
            logger.LogWarning(scheduleEx, "Could not schedule the early IBKR Flex re-check for user {UserId}", userId);
        }
    }

    // Transient kinds only: a rate limit waits for its NotBefore, a not-ready answer has its own retry, a definitive
    // credential failure pauses, and a suspect one is worth a second sighting 30+ minutes on.
    private static bool IsRecheckable(ProviderFailure failure) =>
        failure.Class is FailureClass.Transient or FailureClass.Unknown
        || (failure.Class == FailureClass.Credential && failure.Strength == FailureStrength.Suspect);

    // A statement IBKR has not generated yet is likely to be there within hours, so ask again sooner than the next
    // daily run. The chain is bounded: it ends where the next daily run would take over, and a failure to schedule
    // costs only that earlier look.
    private void ScheduleNotReadyRetry(Guid userId, ProviderFailure failure, int attempt)
    {
        if (failure.Class != FailureClass.NotReady
            || sourceOptions.Get(Provider).NotReadyRetryAfter is not { } delay
            || delay <= TimeSpan.Zero
            || (attempt + 1) * delay >= RecurringInterval)
            return;

        try
        {
            backgroundJobs.Schedule<IbkrFlexIncrementalSyncJob>(job => job.RetryUserAsync(userId, attempt + 1), delay);
            logger.LogInformation(
                "IBKR Flex statement not ready for user {UserId}; asking again in {Delay} (attempt {Attempt})",
                userId,
                delay,
                attempt + 1);
        }
        catch (Exception scheduleEx)
        {
            logger.LogWarning(scheduleEx, "Could not schedule the IBKR Flex retry for user {UserId}", userId);
        }
    }

    private async Task SyncThroughLastClosedDayAsync(Guid userId, DateOnly today)
    {
        var from = today.AddDays(-LookbackDays);
        for (var stepBack = 0; ; stepBack++)
        {
            var window = new FlexStatementWindow(from, today.AddDays(-1 - stepBack));
            try
            {
                await syncService.SyncAsync(userId, window, CancellationToken.None);
                return;
            }
            catch (IbkrFlexException ex) when (ex.IsStatementNotAvailable && stepBack < MaxEndDateStepBacks)
            {
                logger.LogWarning(
                    "IBKR Flex has no statement through {ToDate} for user {UserId} (code {FlexErrorCode}); asking through the day before",
                    window.ToDateWire,
                    userId,
                    ex.FlexErrorCode);
            }
        }
    }

    // The fetcher records fetch failures itself; this covers everything after the fetch (parse,
    // persist) so any Flex failure leaves LastError behind. Best effort: it must not stop the
    // sweep for the remaining users.
    private async Task RecordFailureAsync(IBKRFlexCredential credential, Exception ex)
    {
        try
        {
            credential.RecordUseError(ex.GetBaseException().Message);
            await credentialRepository.SaveLastErrorUnscopedAsync(credential);
        }
        catch (Exception saveEx)
        {
            logger.LogWarning(saveEx, "Failed to record IBKR Flex error for user {UserId}", credential.UserId);
        }
    }

    private static ConnectionHealthSubject HealthSubject(IBKRFlexCredential credential) =>
        new(
            Provider,
            nameof(IBKRFlexCredential),
            credential.Id,
            credential.UserId,
            new DateTimeOffset(DateTime.SpecifyKind(credential.CreatedAt, DateTimeKind.Utc)));

    private Task SaveHealthAsync(IBKRFlexCredential credential, ConnectionHealth health, CancellationToken ct)
    {
        credential.ApplyHealth(health);
        return credentialRepository.SaveHealthUnscopedAsync(credential.Id, health, ct);
    }
}
