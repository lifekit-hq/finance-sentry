using System.Net;
using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Connections;
using FinanceSentry.Infrastructure.Http;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.BrokerageSync.Application.Commands;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.OAuth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;

/// <summary>
/// Scheduled IBKR holdings sync for every user with an active credential. Each attempt is recorded with the
/// connection-health policy (Option B), read by <see cref="IbkrFailureClassifier"/>. Who raises the in-app
/// sync-failure alert depends on <see cref="ConnectionHealthOptions.ActiveProviders"/>:
/// <list type="bullet">
/// <item>With <c>ibkr</c> active, the policy's verdict does: a confirmed credential loss at once, any other
/// failure once it has persisted (by default 3 failures and 2 h without a success).</item>
/// <item>Otherwise (shadow mode) this job's own streak does, as before the policy existed: a non-transient
/// failure at once, a transient one after <see cref="TransientFailureAlertThreshold"/> consecutive ticks.</item>
/// </list>
/// </summary>
public sealed class IBKRSyncJob(
    IIBKRCredentialRepository credentialRepository,
    ICommandHandler<SyncIBKRHoldingsCommand, SyncIBKRHoldingsResult> syncHandler,
    IAlertGeneratorService alerts,
    IUserAlertPreferencesReader userPreferences,
    IJobFailureStreakStore failureStreaks,
    IConnectionHealthShadow connectionHealth,
    IbkrFailureClassifier failureClassifier,
    IOptionsMonitor<ConnectionHealthOptions> healthOptions,
    TimeProvider time,
    ILogger<IBKRSyncJob> logger)
{
    private const string Provider = IbkrFailureClassifier.ProviderName;
    private const string StreakKeyPrefix = "ibkr-sync:";

    // The live session token is cached until near-expiry (~daily), so a failed token
    // request only happens roughly once a day and every subsequent tick (every 15 min)
    // retries. IBKR occasionally returns a fast, self-healing 5xx on that endpoint; one
    // such blip shouldn't page anyone. Escalate only once it persists across several
    // consecutive ticks (~45 min) — that's a real, sticky failure, not a blip.
    private const int TransientFailureAlertThreshold = 3;

    private const string BackendDownMarker = "backend down";

    public async Task ExecuteAsync()
    {
        var policyDrivesAlerts = healthOptions.CurrentValue.IsActive(Provider);
        var activeCredentials = await credentialRepository.GetAllActiveUnscopedAsync();

        foreach (var credential in activeCredentials)
        {
            try
            {
                await syncHandler.Handle(new SyncIBKRHoldingsCommand(credential.UserId), CancellationToken.None);
                var evaluation = await connectionHealth.RecordSuccessAsync(
                    HealthSubject(credential), credential.Health, (health, ct) => SaveHealthAsync(credential, health, ct));

                if (policyDrivesAlerts)
                {
                    await ConnectionHealthSyncFailureAlerts.ResolveIfRecoveredAsync(
                        alerts, userPreferences, logger, credential.UserId, Provider, evaluation);
                }
                else
                {
                    failureStreaks.Set(StreakKey(credential.UserId), JobFailureStreak.Empty);
                    await TryResolveSyncFailureAsync(credential.UserId);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to sync IBKR holdings for user {UserId}",
                    credential.UserId);

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
                else
                {
                    await HandleFailureAsync(credential.UserId, ex);
                }
            }
        }
    }

    private async Task HandleFailureAsync(Guid userId, Exception ex)
    {
        var key = StreakKey(userId);
        var streak = failureStreaks.Get(key);
        var count = streak.Count + 1;

        var transient = IsTransient(ex);
        var shouldAlert = !transient || count >= TransientFailureAlertThreshold;
        var alerted = streak.Alerted || shouldAlert;
        failureStreaks.Set(key, new JobFailureStreak(count, alerted));

        if (shouldAlert && !streak.Alerted)
        {
            await TryGenerateSyncFailureAsync(userId, ex, transient ? SyncFailureClass.Outage : ClassifyPermanent(ex));
        }
    }

    private static string StreakKey(Guid userId) => StreakKeyPrefix + userId;

    private static bool IsTransient(Exception ex)
    {
        if (JobFailureTransientClassifier.IsTransient(ex))
            return true;

        // BrokerAuthException always maps to the same 422 INVALID_CREDENTIALS API error
        // regardless of cause, so a genuine credential/consent failure (401/403) and an
        // upstream IBKR blip (5xx) are only distinguishable via the original status code.
        if (ex is BrokerAuthException { UpstreamStatusCode: { } status } && TransientHttpStatus.IsTransient(status))
            return true;

        // During an outage IBKR answers the token request with 401 and {"error":"backend down"}, so
        // the status alone cannot mark a credential failure; the body can.
        return ex is BrokerAuthException { UpstreamStatusCode: HttpStatusCode.Unauthorized } auth
            && auth.Message.Contains(BackendDownMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static SyncFailureClass ClassifyPermanent(Exception ex) =>
        ex is BrokerAuthException { UpstreamStatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
            ? SyncFailureClass.Credential
            : SyncFailureClass.Unknown;

    private static ConnectionHealthSubject HealthSubject(IBKRCredential credential) =>
        new(Provider, nameof(IBKRCredential), credential.Id, credential.UserId);

    private Task SaveHealthAsync(IBKRCredential credential, ConnectionHealth health, CancellationToken ct)
    {
        credential.ApplyHealth(health);
        return credentialRepository.SaveHealthUnscopedAsync(credential.Id, health, ct);
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

    private async Task TryGenerateSyncFailureAsync(Guid userId, Exception ex, SyncFailureClass failureClass)
    {
        try
        {
            var prefs = await userPreferences.GetAsync(userId);
            if (prefs is null || !prefs.SyncFailureAlerts) return;
            await alerts.GenerateSyncFailureAlertAsync(userId, Provider, null, null, ex.GetType().Name, failureClass);
        }
        catch
        {
            // best-effort
        }
    }
}
