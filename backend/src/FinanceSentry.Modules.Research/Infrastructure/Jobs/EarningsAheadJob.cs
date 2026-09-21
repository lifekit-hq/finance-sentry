namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily Hangfire job (T3, ledger-heartbeat design): raises an EarningsAhead alert for a book or
/// watchlist ticker with earnings landing within <see cref="LookaheadDays"/> days, or an ex-dividend
/// date in the same window. Yahoo quoteSummary is an external dependency with no contract — a
/// missing field, a null date, or a failed fetch already come back as an empty result from
/// <see cref="IEarningsCalendarService"/>, so a provider hiccup here means no alert this run, never a
/// job failure. <see cref="IAlertGeneratorService.GenerateEarningsAheadAlertAsync"/> dedups per
/// (ticker, event type, event date), so re-running the job on the days leading up to the event never
/// re-alerts it — rare, low-noise by design (~0.1 fires/day).
/// </summary>
public sealed class EarningsAheadJob(
    IBankingTotalsReader banking,
    IBrokerageHoldingsReader brokerage,
    IWatchlistReader watchlist,
    IEarningsCalendarService earningsCalendar,
    IAlertGeneratorService alerts,
    ILogger<EarningsAheadJob> logger)
{
    private const string EquityInstrumentType = "STK";
    private const int LookaheadDays = 3;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var userIds = await banking.GetActiveUserIdsAsync(ct);
        if (userIds.Count == 0)
        {
            logger.LogDebug("EarningsAhead: no active users found, skipping.");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var horizon = today.AddDays(LookaheadDays);

        foreach (var userId in userIds)
        {
            try
            {
                await ProcessUserAsync(userId, today, horizon, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "EarningsAhead: error processing user {UserId}", userId);
            }
        }
    }

    private async Task ProcessUserAsync(Guid userId, DateOnly today, DateOnly horizon, CancellationToken ct)
    {
        var tickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var holding in await brokerage.GetHoldingsAsync(userId, ct))
        {
            if (string.Equals(holding.InstrumentType, EquityInstrumentType, StringComparison.OrdinalIgnoreCase))
            {
                tickers.Add(holding.Symbol);
            }
        }

        foreach (var ticker in await watchlist.ListTickersAsync(userId, ct))
        {
            tickers.Add(ticker);
        }

        if (tickers.Count == 0)
        {
            return;
        }

        IReadOnlyList<EarningsEvent> events;
        try
        {
            events = await earningsCalendar.GetForTickersAsync(tickers, today, horizon, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Yahoo quoteSummary has no contract — a failed fetch means no signal this run, not a
            // job failure. The daily cron simply tries again tomorrow.
            logger.LogWarning(ex, "EarningsAhead: earnings-calendar fetch failed for user {UserId}", userId);
            return;
        }

        foreach (var evt in events)
        {
            await RaiseAsync(userId, evt, ct);
        }
    }

    private Task RaiseAsync(Guid userId, EarningsEvent evt, CancellationToken ct)
    {
        var alertEventType = evt.EventType switch
        {
            EarningsEventType.Earnings => EarningsAheadEventType.Earnings,
            EarningsEventType.ExDividend => EarningsAheadEventType.ExDividend,
            // Dividend-payment dates (as opposed to ex-dividend) are not part of this signal.
            _ => (string?)null,
        };

        if (alertEventType is null)
        {
            return Task.CompletedTask;
        }

        return alerts.GenerateEarningsAheadAlertAsync(
            userId, evt.Ticker, alertEventType, evt.EventDate, evt.IsEstimate, ct);
    }
}
