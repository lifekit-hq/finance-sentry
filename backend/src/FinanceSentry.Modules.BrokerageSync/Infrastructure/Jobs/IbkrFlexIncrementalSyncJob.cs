using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Connections;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
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
/// Each attempt is recorded with the connection-health policy (Option B), read by
/// <see cref="IbkrFlexFailureClassifier"/>. With <c>ibkr-flex</c> in
/// <see cref="ConnectionHealthOptions.ActiveProviders"/> the policy's verdict raises and resolves the in-app
/// sync-failure alert (a rejected or expired token at once, anything else once it has persisted: by default
/// 3 failures and 2 h without a success, three daily runs); otherwise (shadow mode) this job raises none,
/// as before the policy existed. IBKR answering that no statement is available yet is no failure at all:
/// it is logged and leaves the credential's error and health as they were.
/// </summary>
public sealed class IbkrFlexIncrementalSyncJob(
    IIBKRFlexCredentialRepository credentialRepository,
    IIbkrFlexTradeSyncService syncService,
    IAlertGeneratorService alerts,
    IUserAlertPreferencesReader userPreferences,
    IConnectionHealthShadow connectionHealth,
    IbkrFlexFailureClassifier failureClassifier,
    IOptionsMonitor<ConnectionHealthOptions> healthOptions,
    TimeProvider time,
    ILogger<IbkrFlexIncrementalSyncJob> logger)
{
    private const int LookbackDays = 7;
    private const string Provider = IbkrFlexFailureClassifier.ProviderName;

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
        var window = new FlexStatementWindow(today.AddDays(-LookbackDays), today);

        foreach (var credential in activeCredentials)
        {
            try
            {
                await syncService.SyncAsync(credential.UserId, window, CancellationToken.None, statementNotAvailableIsExpected: true);
                var evaluation = await connectionHealth.RecordSuccessAsync(
                    HealthSubject(credential), credential.Health, (health, ct) => SaveHealthAsync(credential, health, ct));
                if (policyDrivesAlerts)
                {
                    await ConnectionHealthSyncFailureAlerts.ResolveIfRecoveredAsync(
                        alerts, userPreferences, logger, credential.UserId, Provider, evaluation);
                }
            }
            catch (IbkrFlexException ex) when (ex.IsStatementNotAvailable)
            {
                logger.LogInformation(
                    "IBKR Flex has no statement available yet for user {UserId} ({FlexMessage}); trying again next run",
                    credential.UserId,
                    ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync IBKR Flex trades for user {UserId}", credential.UserId);
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
