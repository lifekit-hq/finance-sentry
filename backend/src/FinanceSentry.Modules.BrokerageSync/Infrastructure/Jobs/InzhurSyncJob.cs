using System.Runtime.ExceptionServices;
using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;

/// <summary>
/// The once-a-day read of each connected Inzhur cabinet (design report §4): refresh the session, read holdings and
/// cash, store them. It never signs in — a broken refresh chain flips the connection to reauth_required and tells
/// the owner, who reconnects from the app. Inzhur being busy or down (429/5xx/network) is retried by Hangfire once
/// after an hour and once more four hours later, then the day is skipped and the job fails; nothing else retries.
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

    /// <summary>Seconds before each retry of a transient failure (1 h, then 4 h); past the last one the day is skipped.</summary>
    public const int FirstRetryDelaySeconds = 3600;
    public const int SecondRetryDelaySeconds = 14400;

    /// <summary>Retries Hangfire makes of one user's read after a transient failure.</summary>
    public const int RetryAttempts = 2;

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

            backgroundJobs.Enqueue<InzhurSyncJob>(j => j.SyncUserAsync(credential.UserId, null!));
        }
    }

    /// <summary>
    /// One user's read. A transient failure (Inzhur busy or down) is rethrown so Hangfire retries it after the declared
    /// delays; when the last retry fails too the outage alert is raised and the job ends in the Failed state.
    /// </summary>
    [AutomaticRetry(Attempts = RetryAttempts, DelaysInSeconds = [FirstRetryDelaySeconds, SecondRetryDelaySeconds], OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task SyncUserAsync(Guid userId, PerformContext context)
    {
        var credential = await credentialRepository.GetByUserIdUnscopedAsync(userId);
        if (credential is null || credential.Status != InzhurConnectionStatus.Active)
            return;

        var retriesMade = context.GetJobParameter<int>("RetryCount");
        await SyncOneAsync(credential, isLastAttempt: retriesMade >= RetryAttempts);
    }

    private async Task SyncOneAsync(InzhurCredential credential, bool isLastAttempt)
    {
        ExceptionDispatchInfo? retry = null;
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
                if (isLastAttempt)
                {
                    logger.LogError(
                        "Inzhur sync for user {UserId} failed after {Retries} retries; skipping the day",
                        credential.UserId, RetryAttempts);
                    await TryGenerateSyncFailureAsync(credential.UserId, failure.Kind.ToString(), SyncFailureClass.Outage);
                }

                retry = ExceptionDispatchInfo.Capture(ex);
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

        // Rethrown outside the catch so the health record above is written first; Hangfire then schedules the retry
        // (or, on the last attempt, marks the job Failed).
        retry?.Throw();
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
