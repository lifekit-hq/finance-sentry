using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;

/// <summary>
/// The once-a-day read of each connected Inzhur cabinet (design report §4): refresh the session, read holdings and
/// cash, store them. It never signs in — a broken refresh chain flips the connection to reauth_required and tells
/// the owner, who reconnects from the app. Inzhur being busy or down (429/5xx/network) is retried once after an hour
/// and once more four hours later, then the day is skipped; nothing else retries.
/// </summary>
public sealed class InzhurSyncJob(
    IInzhurCredentialRepository credentialRepository,
    IInzhurSyncService syncService,
    IBackgroundJobClient backgroundJobs,
    IAlertGeneratorService alerts,
    IUserAlertPreferencesReader userPreferences,
    IConnectionHealthShadow connectionHealth,
    TimeProvider clock,
    ILogger<InzhurSyncJob> logger)
{
    private const string Provider = InzhurHoldingsMapper.Provider;

    /// <summary>A connection read this recently (e.g. right after a reconnect) is not read again by the daily run.</summary>
    public static readonly TimeSpan RecentSyncWindow = TimeSpan.FromHours(6);

    /// <summary>Delays before each retry of a transient failure; past the last one the day is skipped.</summary>
    public static readonly IReadOnlyList<TimeSpan> RetryDelays = [TimeSpan.FromHours(1), TimeSpan.FromHours(4)];

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task ExecuteAsync()
    {
        var active = await credentialRepository.GetAllActiveUnscopedAsync();
        var now = clock.GetUtcNow().UtcDateTime;

        foreach (var credential in active)
        {
            if (credential.LastSyncAt is { } last && now - last < RecentSyncWindow)
            {
                logger.LogInformation("Inzhur for user {UserId} was read at {LastSyncAt:o}; not reading again today", credential.UserId, last);
                continue;
            }

            await SyncOneAsync(credential, attempt: 0);
        }
    }

    /// <summary>A scheduled retry after a transient failure; <paramref name="attempt"/> counts retries already made.</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RetryAsync(Guid userId, int attempt)
    {
        var credential = await credentialRepository.GetByUserIdUnscopedAsync(userId);
        if (credential is null || credential.Status != InzhurConnectionStatus.Active)
            return;

        await SyncOneAsync(credential, attempt);
    }

    private async Task SyncOneAsync(InzhurCredential credential, int attempt)
    {
        try
        {
            if (await syncService.SyncAsync(credential.UserId) == InzhurSyncOutcome.Skipped)
                return;

            await TryResolveSyncFailureAsync(credential.UserId);
            await connectionHealth.RecordSuccessAsync(
                HealthSubject(credential), credential.Health, (health, ct) => SaveHealthAsync(credential, health, ct));
        }
        catch (Exception ex)
        {
            var failure = ex as InzhurApiException;
            logger.LogWarning(
                "Inzhur sync failed for user {UserId}: {FailureKind} ({ErrorType})",
                credential.UserId, failure?.Kind.ToString() ?? "Unexpected", ex.GetType().Name);

            if (failure is { IsTransient: true })
            {
                if (attempt < RetryDelays.Count)
                    backgroundJobs.Schedule<InzhurSyncJob>(j => j.RetryAsync(credential.UserId, attempt + 1), RetryDelays[attempt]);
                else
                    await TryGenerateSyncFailureAsync(credential.UserId, failure.Kind.ToString(), SyncFailureClass.Outage);
            }
            else if (failure is { Kind: InzhurFailureKind.ReauthRequired })
            {
                await TryGenerateSyncFailureAsync(credential.UserId, InzhurConnectionStatus.ReauthRequired, SyncFailureClass.Credential);
            }
            else
            {
                await TryGenerateSyncFailureAsync(credential.UserId, ex.GetType().Name, SyncFailureClass.Unknown);
            }

            await connectionHealth.RecordFailureAsync(
                HealthSubject(credential), credential.Health, ToProviderFailure(ex), (health, ct) => SaveHealthAsync(credential, health, ct));
        }
    }

    private static ConnectionHealthSubject HealthSubject(InzhurCredential credential) =>
        new(Provider, nameof(InzhurCredential), credential.Id, credential.UserId);

    private Task SaveHealthAsync(InzhurCredential credential, ConnectionHealth health, CancellationToken ct)
    {
        credential.ApplyHealth(health);
        return credentialRepository.SaveHealthUnscopedAsync(credential.Id, health, ct);
    }

    // Inzhur's refresh answers are unambiguous (the cabinet itself signs the user out on them), so a dead session is
    // definitive rather than suspect.
    private static ProviderFailure ToProviderFailure(Exception ex) => ex switch
    {
        InzhurApiException { Kind: InzhurFailureKind.ReauthRequired } => ProviderFailure.CredentialDefinitive("SESSION_EXPIRED"),
        InzhurApiException { IsTransient: true } api => ProviderFailure.Transient(api.Kind.ToString()),
        _ => ProviderFailure.Unknown(ex.GetType().Name),
    };

    private async Task TryResolveSyncFailureAsync(Guid userId)
    {
        try
        {
            var prefs = await userPreferences.GetAsync(userId);
            if (prefs is null || !prefs.SyncFailureAlerts) return;
            await alerts.ResolveSyncFailureAlertAsync(userId, Provider, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve the Inzhur sync alert for user {UserId}", userId);
        }
    }

    private async Task TryGenerateSyncFailureAsync(Guid userId, string errorCode, SyncFailureClass failureClass)
    {
        try
        {
            var prefs = await userPreferences.GetAsync(userId);
            if (prefs is null || !prefs.SyncFailureAlerts) return;
            await alerts.GenerateSyncFailureAlertAsync(userId, Provider, null, null, errorCode, failureClass);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not raise the Inzhur sync alert for user {UserId}", userId);
        }
    }
}
