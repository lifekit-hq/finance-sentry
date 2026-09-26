using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;

/// <summary>
/// One-shot historical backfill of IBKR trade/cash-transaction activity via the Flex Web
/// Service (fs-435 S5), walking year-by-year windows from <see cref="BackfillStartYear"/> to
/// today — IBKR caps a single Flex pull at 365 days, so a multi-year range must be split.
/// Registered with <c>Cron.Never()</c> (dashboard-trigger only): this is a manual, one-time
/// operation, not a recurring job. A clean no-op, logged and returned from, when no user has
/// an active Flex credential — supplying one is a manual step in IBKR's account management.
/// </summary>
public sealed class IbkrFlexBackfillJob(
    IIBKRFlexCredentialRepository credentialRepository,
    IIbkrFlexTradeSyncService syncService,
    ILogger<IbkrFlexBackfillJob> logger)
{
    private const int BackfillStartYear = 2022;

    public async Task ExecuteAsync()
    {
        var activeCredentials = await credentialRepository.GetAllActiveAsync();
        if (activeCredentials.Count == 0)
        {
            logger.LogInformation("No active IBKR Flex credentials; skipping trade history backfill.");
            return;
        }

        foreach (var credential in activeCredentials)
        {
            foreach (var window in BuildYearlyWindows())
            {
                try
                {
                    await syncService.SyncAsync(credential.UserId, window, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to backfill IBKR Flex trades for user {UserId}, window {FromDate}-{ToDate}",
                        credential.UserId, window.FromDateWire, window.ToDateWire);
                }
            }
        }
    }

    /// <summary>Splits [<see cref="BackfillStartYear"/>-01-01, today] into ≤365-day, calendar-year-aligned windows.</summary>
    public static IReadOnlyList<FlexStatementWindow> BuildYearlyWindows(DateOnly? asOf = null)
    {
        var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var windows = new List<FlexStatementWindow>();

        for (var year = BackfillStartYear; year <= today.Year; year++)
        {
            var from = new DateOnly(year, 1, 1);
            var to = year == today.Year ? today : new DateOnly(year, 12, 31);
            windows.Add(new FlexStatementWindow(from, to));
        }

        return windows;
    }
}
