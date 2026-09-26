using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Infrastructure.Retry;
using FinanceSentry.Modules.BrokerageSync.Application.Commands;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;

public sealed class IBKRSyncJob(
    IIBKRCredentialRepository credentialRepository,
    ICommandHandler<SyncIBKRHoldingsCommand, SyncIBKRHoldingsResult> syncHandler,
    IAlertGeneratorService alerts,
    IUserAlertPreferencesReader userPreferences,
    IJobFailureStreakStore failureStreaks,
    ILogger<IBKRSyncJob> logger)
{
    private const string Provider = "ibkr";
    private const string StreakKeyPrefix = "ibkr-sync:";

    // The live session token is cached until near-expiry (~daily), so a failed token
    // request only happens roughly once a day and every subsequent tick (every 15 min)
    // retries. IBKR occasionally returns a fast, self-healing 5xx on that endpoint; one
    // such blip shouldn't page anyone. Escalate only once it persists across several
    // consecutive ticks (~45 min) — that's a real, sticky failure, not a blip.
    private const int TransientFailureAlertThreshold = 3;

    public async Task ExecuteAsync()
    {
        var activeCredentials = await credentialRepository.GetAllActiveAsync();

        foreach (var credential in activeCredentials)
        {
            try
            {
                await syncHandler.Handle(new SyncIBKRHoldingsCommand(credential.UserId), CancellationToken.None);
                failureStreaks.Set(StreakKey(credential.UserId), JobFailureStreak.Empty);
                await TryResolveSyncFailureAsync(credential.UserId);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to sync IBKR holdings for user {UserId}",
                    credential.UserId);

                await HandleFailureAsync(credential.UserId, ex);
            }
        }
    }

    private async Task HandleFailureAsync(Guid userId, Exception ex)
    {
        var key = StreakKey(userId);
        var streak = failureStreaks.Get(key);
        var count = streak.Count + 1;

        var shouldAlert = !IsTransient(ex) || count >= TransientFailureAlertThreshold;
        var alerted = streak.Alerted || shouldAlert;
        failureStreaks.Set(key, new JobFailureStreak(count, alerted));

        if (shouldAlert && !streak.Alerted)
            await TryGenerateSyncFailureAsync(userId, ex);
    }

    private static string StreakKey(Guid userId) => StreakKeyPrefix + userId;

    private static bool IsTransient(Exception ex)
    {
        if (JobFailureTransientClassifier.IsTransient(ex))
            return true;

        // BrokerAuthException always maps to the same 422 INVALID_CREDENTIALS API error
        // regardless of cause, so a genuine credential/consent failure (401/403) and an
        // upstream IBKR blip (5xx) are only distinguishable via the original status code.
        return ex is BrokerAuthException { UpstreamStatusCode: { } status } && RetryPolicies.IsTransientHttpError(status);
    }

    private async Task TryResolveSyncFailureAsync(Guid userId)
    {
        try
        {
            var prefs = await userPreferences.GetAsync(userId);
            if (prefs is null || !prefs.SyncFailureAlerts) return;
            await alerts.ResolveSyncFailureAlertAsync(userId, Provider, null);
        }
        catch
        {
            // best-effort
        }
    }

    private async Task TryGenerateSyncFailureAsync(Guid userId, Exception ex)
    {
        try
        {
            var prefs = await userPreferences.GetAsync(userId);
            if (prefs is null || !prefs.SyncFailureAlerts) return;
            await alerts.GenerateSyncFailureAlertAsync(userId, Provider, null, null, ex.GetType().Name);
        }
        catch
        {
            // best-effort
        }
    }
}
