using System.Net;
using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Connections;
using FinanceSentry.Infrastructure.Http;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.CryptoSync.Application.Commands;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;
using FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Jobs;

/// <summary>
/// Scheduled holdings sync for one venue, for every user with an active credential there. A user whose
/// sync fails does not cost the others their sync. Each attempt is recorded with the connection-health
/// policy (Option B), read by the venue's <see cref="IProviderFailureClassifier"/>.
///
/// Shadow mode (the venue is not in <see cref="ConnectionHealthOptions.ActiveProviders"/>), as before the
/// policy existed: the user gets an in-app sync-failure alert at once when the venue rejected the
/// credential, but only after <see cref="TransientFailureAlertThreshold"/> consecutive ticks when it was a
/// throttle, 5xx, timeout or network failure (the same per-user streak shape as <c>IBKRSyncJob</c>). A run
/// in which EVERY user failed produced nothing, so it fails the job: that is what
/// <c>ConsecutiveFailureGaugeFilter</c> counts toward the job's failure-streak gauge.
///
/// With the venue active, the policy's verdict is the one signal a failure raises: the sync-failure alert,
/// at once for a confirmed credential loss, otherwise once the failure has persisted (by default 3 failures
/// and 2 h without a success). The run itself then succeeds even when every user failed.
///
/// One subclass per venue, because Hangfire names a job by its type: each venue gets its own
/// schedule, metrics and failure streak.
/// </summary>
public abstract class CryptoExchangeSyncJob(
    IExchangeCredentialRepository credentialRepository,
    ICommandHandler<SyncExchangeHoldingsCommand, SyncExchangeHoldingsResult> syncHandler,
    IAlertGeneratorService alerts,
    IUserAlertPreferencesReader userPreferences,
    IJobFailureStreakStore failureStreaks,
    IConnectionHealthShadow connectionHealth,
    IProviderFailureClassifier failureClassifier,
    IOptionsMonitor<ConnectionHealthOptions> healthOptions,
    TimeProvider time,
    ILogger logger)
{
    // A venue blip heals within a tick or two; 15-minute ticks make three of them ~45 minutes, which
    // is a sticky outage rather than noise.
    private const int TransientFailureAlertThreshold = 3;

    protected abstract string Provider { get; }

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync()
    {
        var policyDrivesAlerts = healthOptions.CurrentValue.IsActive(Provider);
        var activeCredentials = await credentialRepository.GetAllActiveUnscopedAsync(Provider);
        var failures = new List<Exception>();

        foreach (var credential in activeCredentials)
        {
            try
            {
                await syncHandler.Handle(
                    new SyncExchangeHoldingsCommand(credential.UserId, Provider), CancellationToken.None);
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
                    "Failed to sync {Provider} holdings for user {UserId}",
                    Provider,
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
                    failures.Add(ex);
                    await HandleFailureAsync(credential.UserId, ex);
                }
            }
        }

        if (failures.Count > 0 && failures.Count == activeCredentials.Count)
        {
            throw new AggregateException(
                $"{CryptoExchangeProvider.DisplayName(Provider)} holdings sync failed for all "
                + $"{failures.Count} connected user(s).",
                failures);
        }
    }

    private async Task HandleFailureAsync(Guid userId, Exception ex)
    {
        var key = StreakKey(userId);
        var streak = failureStreaks.Get(key);
        var count = streak.Count + 1;

        var cause = ex is CryptoTradeHistoryException { InnerException: { } inner } ? inner : ex;
        var transient = IsTransient(cause);
        var shouldAlert = !transient || count >= TransientFailureAlertThreshold;
        failureStreaks.Set(key, new JobFailureStreak(count, streak.Alerted || shouldAlert));

        if (shouldAlert && !streak.Alerted)
        {
            await TryGenerateSyncFailureAsync(userId, ex, transient ? SyncFailureClass.Outage : ClassifyPermanent(cause));
        }
    }

    private string StreakKey(Guid userId) => $"crypto-sync:{Provider}:{userId}";

    private static bool IsTransient(Exception ex)
    {
        if (JobFailureTransientClassifier.IsTransient(ex))
            return true;

        // Binance keeps its status on the exception rather than as an HttpRequestException; a
        // Revolut X HTTP failure is already covered through its inner HttpRequestException.
        return ex is BinanceException { VenueStatusCode: { } status }
            && TransientHttpStatus.IsTransient((HttpStatusCode)status);
    }

    private static SyncFailureClass ClassifyPermanent(Exception ex)
    {
        var credentialRejected = ex switch
        {
            BinanceException binance =>
                binance.VenueStatusCode is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden
                || (binance.BinanceErrorCode is { } code && BinanceFailureClassifier.CredentialErrorCodes.Contains(code)),
            RevolutXException revolut =>
                revolut.VenueStatusCode is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden,
            _ => false,
        };

        return credentialRejected ? SyncFailureClass.Credential : SyncFailureClass.Unknown;
    }

    private ConnectionHealthSubject HealthSubject(ExchangeCredential credential) =>
        new(Provider, nameof(ExchangeCredential), credential.Id, credential.UserId);

    private Task SaveHealthAsync(ExchangeCredential credential, ConnectionHealth health, CancellationToken ct)
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

public sealed class BinanceSyncJob(
    IExchangeCredentialRepository credentialRepository,
    ICommandHandler<SyncExchangeHoldingsCommand, SyncExchangeHoldingsResult> syncHandler,
    IAlertGeneratorService alerts,
    IUserAlertPreferencesReader userPreferences,
    IJobFailureStreakStore failureStreaks,
    IConnectionHealthShadow connectionHealth,
    BinanceFailureClassifier failureClassifier,
    IOptionsMonitor<ConnectionHealthOptions> healthOptions,
    TimeProvider time,
    ILogger<BinanceSyncJob> logger)
    : CryptoExchangeSyncJob(
        credentialRepository, syncHandler, alerts, userPreferences, failureStreaks, connectionHealth,
        failureClassifier, healthOptions, time, logger)
{
    protected override string Provider => CryptoExchangeProvider.Binance;
}

public sealed class RevolutXSyncJob(
    IExchangeCredentialRepository credentialRepository,
    ICommandHandler<SyncExchangeHoldingsCommand, SyncExchangeHoldingsResult> syncHandler,
    IAlertGeneratorService alerts,
    IUserAlertPreferencesReader userPreferences,
    IJobFailureStreakStore failureStreaks,
    IConnectionHealthShadow connectionHealth,
    RevolutXFailureClassifier failureClassifier,
    IOptionsMonitor<ConnectionHealthOptions> healthOptions,
    TimeProvider time,
    ILogger<RevolutXSyncJob> logger)
    : CryptoExchangeSyncJob(
        credentialRepository, syncHandler, alerts, userPreferences, failureStreaks, connectionHealth,
        failureClassifier, healthOptions, time, logger)
{
    protected override string Provider => CryptoExchangeProvider.RevolutX;
}
