using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;

/// <summary>
/// Daily recurring pull of recent IBKR trade/cash-transaction activity via the Flex Web
/// Service (fs-435 S5). Pulls a short trailing window rather than the saved query's full
/// default range, since re-pulling an overlapping window is a cheap no-op (idempotent on
/// execution id / cash transaction hash) — the one-shot <see cref="IbkrFlexBackfillJob"/>
/// covers full history. A clean no-op, logged and returned from, when no user has an active
/// Flex credential — supplying one is a manual step in IBKR's account management.
/// </summary>
public sealed class IbkrFlexIncrementalSyncJob(
    IIBKRFlexCredentialRepository credentialRepository,
    IIbkrFlexTradeSyncService syncService,
    ILogger<IbkrFlexIncrementalSyncJob> logger)
{
    private const int LookbackDays = 7;

    public async Task ExecuteAsync()
    {
        var activeCredentials = await credentialRepository.GetAllActiveUnscopedAsync();
        if (activeCredentials.Count == 0)
        {
            logger.LogInformation("No active IBKR Flex credentials; skipping incremental trade sync.");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var window = new FlexStatementWindow(today.AddDays(-LookbackDays), today);

        foreach (var credential in activeCredentials)
        {
            try
            {
                await syncService.SyncAsync(credential.UserId, window, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync IBKR Flex trades for user {UserId}", credential.UserId);
                await RecordFailureAsync(credential, ex);
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
}
