using System.Globalization;
using System.Net;
using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Infrastructure.Retry;
using FinanceSentry.Modules.CryptoSync.Application.Commands;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Jobs;

/// <summary>
/// Scheduled holdings sync for one venue, for every user with an active credential there.
///
/// A user whose sync fails does not cost the others their sync, and gets an in-app sync-failure
/// alert at once when the venue rejected the credential, but only after
/// <see cref="TransientFailureAlertThreshold"/> consecutive ticks when it was a throttle, 5xx,
/// timeout or network failure (the same per-user streak shape as <c>IBKRSyncJob</c>).
///
/// A run in which EVERY user failed produced nothing, so it fails the job: that is what
/// <c>ConsecutiveFailureAlertFilter</c> watches to raise the Telegram alert (#023). Swallowing it
/// would make a dead key or a venue outage indistinguishable from a quiet run.
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
    ILogger logger)
{
    // A venue blip heals within a tick or two; 15-minute ticks make three of them ~45 minutes, which
    // is a sticky outage rather than noise.
    private const int TransientFailureAlertThreshold = 3;

    protected abstract string Provider { get; }

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync()
    {
        var activeCredentials = await credentialRepository.GetAllActiveUnscopedAsync(Provider);
        var failures = new List<Exception>();

        foreach (var credential in activeCredentials)
        {
            try
            {
                await syncHandler.Handle(
                    new SyncExchangeHoldingsCommand(credential.UserId, Provider), CancellationToken.None);
                failureStreaks.Set(StreakKey(credential.UserId), JobFailureStreak.Empty);
                await TryResolveSyncFailureAsync(credential.UserId);
                await connectionHealth.RecordSuccessAsync(
                    HealthSubject(credential), credential.Health, (health, ct) => SaveHealthAsync(credential, health, ct));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to sync {Provider} holdings for user {UserId}",
                    Provider,
                    credential.UserId);

                failures.Add(ex);
                await HandleFailureAsync(credential.UserId, ex);
                await connectionHealth.RecordFailureAsync(
                    HealthSubject(credential),
                    credential.Health,
                    ToProviderFailure(ex),
                    (health, ct) => SaveHealthAsync(credential, health, ct));
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
            && RetryPolicies.IsTransientHttpError((HttpStatusCode)status);
    }

    // Binance codes for a key the venue will not honour: -1002 unauthorized, -1022 bad signature,
    // -2008 unknown key id, -2014 malformed key, -2015 rejected key / IP / permissions.
    private static readonly HashSet<int> BinanceCredentialErrorCodes = [-1002, -1022, -2008, -2014, -2015];

    private static SyncFailureClass ClassifyPermanent(Exception ex)
    {
        var credentialRejected = ex switch
        {
            BinanceException binance =>
                binance.VenueStatusCode is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden
                || (binance.BinanceErrorCode is { } code && BinanceCredentialErrorCodes.Contains(code)),
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

    /// <summary>
    /// Shadow mode (Option B, S1): this job's own failure decision, read into the policy's taxonomy so the
    /// policy's verdict can be compared with the alert this job raises. The venue classifiers (S3) replace it.
    /// A Binance invalid-key code is definitive; a bare 401/403 is only suspect (report §5).
    /// </summary>
    private static ProviderFailure ToProviderFailure(Exception ex)
    {
        var cause = ex is CryptoTradeHistoryException { InnerException: { } inner } ? inner : ex;
        var code = cause switch
        {
            BinanceException { BinanceErrorCode: { } binanceCode } => binanceCode.ToString(CultureInfo.InvariantCulture),
            BinanceException { VenueStatusCode: { } status } => $"HTTP_{status}",
            RevolutXException { VenueStatusCode: { } status } => $"HTTP_{status}",
            _ => cause.GetType().Name,
        };

        if (IsTransient(cause))
            return ProviderFailure.Transient(code);

        if (cause is BinanceException { BinanceErrorCode: { } errorCode } && BinanceCredentialErrorCodes.Contains(errorCode))
            return ProviderFailure.CredentialDefinitive(code);

        return ClassifyPermanent(cause) == SyncFailureClass.Credential
            ? ProviderFailure.CredentialSuspect(code)
            : ProviderFailure.Unknown(code);
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
    ILogger<BinanceSyncJob> logger)
    : CryptoExchangeSyncJob(credentialRepository, syncHandler, alerts, userPreferences, failureStreaks, connectionHealth, logger)
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
    ILogger<RevolutXSyncJob> logger)
    : CryptoExchangeSyncJob(credentialRepository, syncHandler, alerts, userPreferences, failureStreaks, connectionHealth, logger)
{
    protected override string Provider => CryptoExchangeProvider.RevolutX;
}
