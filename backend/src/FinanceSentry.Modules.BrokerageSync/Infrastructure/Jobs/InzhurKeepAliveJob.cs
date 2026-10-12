using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;

/// <summary>
/// Keeps each connected Inzhur session alive between the daily reads. The cabinet's refresh chain only survives if the
/// refresh token is used again within its lifetime, and the daily read comes once in 24 h; this refreshes the session
/// every <see cref="InzhurOptions.KeepAliveInterval"/> (hourly at the defaults), stores the rotated jar and reads
/// nothing. It never signs in, so it spends no sign-in allowance. A run that finds Inzhur busy or down changes nothing:
/// the next run is the retry. A dead session is handled as in the daily read: the connection flips to
/// reauth_required and the owner is told.
/// </summary>
public sealed class InzhurKeepAliveJob(
    IInzhurCredentialRepository credentialRepository,
    IInzhurKeepAliveService keepAlive,
    IBackgroundJobClient backgroundJobs,
    IAlertGeneratorService alerts,
    IUserAlertPreferencesReader userPreferences,
    IConnectionHealthShadow connectionHealth,
    IOptions<InzhurOptions> options,
    TimeProvider clock,
    ILogger<InzhurKeepAliveJob> logger)
{
    private const string Provider = InzhurHoldingsMapper.Provider;

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task ExecuteAsync()
    {
        var active = await credentialRepository.GetAllActiveUnscopedAsync();
        var now = clock.GetUtcNow().UtcDateTime;
        var refreshedRecently = options.Value.KeepAliveInterval / 2;

        foreach (var credential in active)
        {
            // The daily read or a reconnect just refreshed it: nothing to keep alive yet.
            if (credential.SessionRefreshedAt is { } last && now - last < refreshedRecently)
                continue;

            backgroundJobs.Enqueue<InzhurKeepAliveJob>(j => j.KeepAliveUserAsync(credential.UserId));
        }
    }

    /// <summary>One user's refresh. Not retried by Hangfire: the next scheduled run is the retry.</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task KeepAliveUserAsync(Guid userId)
    {
        try
        {
            await keepAlive.KeepAliveAsync(userId);
        }
        catch (InzhurApiException ex) when (ex.IsTransient)
        {
            logger.LogWarning(
                "Inzhur keep-alive for user {UserId} found Inzhur {FailureKind}; the connection is unchanged and the next run retries",
                userId, ex.Kind);
        }
        catch (InzhurApiException ex) when (ex.Kind == InzhurFailureKind.ReauthRequired)
        {
            await RecordSessionEndedAsync(userId);
        }
    }

    private async Task RecordSessionEndedAsync(Guid userId)
    {
        await TryGenerateAlertAsync(userId);

        var credential = await credentialRepository.GetByUserIdUnscopedAsync(userId);
        if (credential is null)
            return;

        await connectionHealth.RecordFailureAsync(
            new ConnectionHealthSubject(Provider, nameof(InzhurCredential), credential.Id, userId),
            credential.Health,
            ProviderFailure.CredentialDefinitive("SESSION_EXPIRED"),
            (health, ct) =>
            {
                credential.ApplyHealth(health);
                return credentialRepository.SaveHealthUnscopedAsync(credential.Id, health, ct);
            });
    }

    private async Task TryGenerateAlertAsync(Guid userId)
    {
        try
        {
            var prefs = await userPreferences.GetAsync(userId);
            if (prefs is null || !prefs.SyncFailureAlerts) return;
            await alerts.GenerateSyncFailureAlertAsync(
                userId, Provider, null, null, InzhurConnectionStatus.ReauthRequired, SyncFailureClass.Credential);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not raise the Inzhur sync alert for user {UserId}", userId);
        }
    }
}
