using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.CryptoSync.Application.Commands;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Jobs;

/// <summary>
/// Scheduled holdings sync for one venue, for every user with an active credential there.
///
/// A user whose sync fails gets an in-app sync-failure alert and does not cost the others their
/// sync. A run in which EVERY user failed produced nothing, so it fails the job: that is what
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
    ILogger logger)
{
    protected abstract string Provider { get; }

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync()
    {
        var activeCredentials = await credentialRepository.GetAllActiveAsync(Provider);
        var failures = new List<Exception>();

        foreach (var credential in activeCredentials)
        {
            try
            {
                await syncHandler.Handle(
                    new SyncExchangeHoldingsCommand(credential.UserId, Provider), CancellationToken.None);
                await TryResolveSyncFailureAsync(credential.UserId);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to sync {Provider} holdings for user {UserId}",
                    Provider,
                    credential.UserId);

                failures.Add(ex);
                await TryGenerateSyncFailureAsync(credential.UserId, ex);
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

public sealed class BinanceSyncJob(
    IExchangeCredentialRepository credentialRepository,
    ICommandHandler<SyncExchangeHoldingsCommand, SyncExchangeHoldingsResult> syncHandler,
    IAlertGeneratorService alerts,
    IUserAlertPreferencesReader userPreferences,
    ILogger<BinanceSyncJob> logger)
    : CryptoExchangeSyncJob(credentialRepository, syncHandler, alerts, userPreferences, logger)
{
    protected override string Provider => CryptoExchangeProvider.Binance;
}

public sealed class RevolutXSyncJob(
    IExchangeCredentialRepository credentialRepository,
    ICommandHandler<SyncExchangeHoldingsCommand, SyncExchangeHoldingsResult> syncHandler,
    IAlertGeneratorService alerts,
    IUserAlertPreferencesReader userPreferences,
    ILogger<RevolutXSyncJob> logger)
    : CryptoExchangeSyncJob(credentialRepository, syncHandler, alerts, userPreferences, logger)
{
    protected override string Provider => CryptoExchangeProvider.RevolutX;
}
