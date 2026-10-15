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
/// recorded as not ready: the policy does not count it while the last success is younger than the source's
/// <see cref="ConnectionHealthSourceOptions.NotReadyDeadline"/> (48 h by default), and the job asks again sooner
/// than the next daily run (<see cref="ConnectionHealthSourceOptions.NotReadyRetryAfter"/>, 2 h by default),
/// through <see cref="RetryUserAsync"/>, until the next daily run is due.
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

            await SyncCredentialAsync(credential, today, policyDrivesAlerts, attempt: 0);
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
        await SyncCredentialAsync(credential, today, healthOptions.CurrentValue.IsActive(Provider), attempt);
    }

    private async Task SyncCredentialAsync(IBKRFlexCredential credential, DateOnly today, bool policyDrivesAlerts, int attempt)
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
        }
    }

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
        new(Provider, nameof(IBKRFlexCredential), credential.Id, credential.UserId);

    private Task SaveHealthAsync(IBKRFlexCredential credential, ConnectionHealth health, CancellationToken ct)
    {
        credential.ApplyHealth(health);
        return credentialRepository.SaveHealthUnscopedAsync(credential.Id, health, ct);
    }
}
