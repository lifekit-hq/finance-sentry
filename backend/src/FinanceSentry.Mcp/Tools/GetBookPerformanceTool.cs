using System.ComponentModel;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetBookPerformanceTool(
    IBookPerformanceService performance,
    IBookIncomeService income,
    IIdentityResolver identity,
    ILogger<GetBookPerformanceTool>? logger = null)
{
    [McpServerTool(Name = "get_book_performance")]
    [Description("Returns the brokerage book's time-weighted return (TWR) versus the SPY benchmark's adjusted-close (total) return over the identical window for one or more lookback windows (1W, 1M, 3M, 1Y). bookTwr chains the daily brokerage totals (IBKR + Inzhur, idle broker cash included, so purchases from cash are not gains) with the deposits and withdrawals the brokers report taken out, and the day a provider was linked bridged as capital; bookTwrFrom / bookTwrTo are the snapshot dates it runs between, and spyTwr is SPY over those same dates, so delta (book minus SPY) and the verdict (outperform / underperform / inline) are like for like. When there is no book window, spyTwr runs from the first bar on or after since and delta is null. bookTwrReasons qualify bookTwr: UNKNOWN_FLOW names a provider that has never reported a deposit or withdrawal (its deposits still read as return, so the figure is an estimate - say so when quoting it), SCOPE_CHANGED says a link was bridged, plus STALE_SOURCE, BACKFILLED_ESTIMATE, FX_TODAY_RATE, FX_UNKNOWN_1TO1 from the end rows and flow conversions. Each period's book is a separate figure, the invested value's plain change (deposits included), as a typed headline record: method VALUE_CHANGE, basis START_VALUE, scope invested_brokerage, window, amount (the USD change), percent, quality (COMPLETE / ESTIMATED / DEGRADED / UNAVAILABLE) and typed reasons (STALE_SOURCE, BACKFILLED_ESTIMATE, FX_TODAY_RATE, FX_UNKNOWN_1TO1, NO_SPLIT when the period has snapshot days but no measured cash/invested split to form the window, SHORT_HISTORY when there is no measurable window otherwise, ZERO_BASE when the start value is zero, SCOPE_CHANGED when a provider was linked inside the window); read quality before quoting it. Each period also carries the flow-neutral holdings-based return: holdingsBasedReturn (the brokerage book's price return from revaluing each capture's quantities at the next capture's prices and chaining, so deposits and purchases from idle cash read as 0%; excludes dividends) against spyPriceReturn (SPY's change in close, not adjusted close, between the first and last capture the index covers), with holdingsBasedDelta (difference). When there is no number, holdingsBasedUnavailableReason says why (InsufficientCaptures, WindowNotCovered, MissingPrice, NoInvestedBase; never 0) and holdingsBasedMissingPriceSymbols names the positions without a price. Periods with insufficient price or value history are omitted. Defaults to all four windows and the authenticated MCP identity. qqqTwr is QQQ's adjusted-close return over the same bookTwrFrom to bookTwrTo window as spyTwr and qqqDelta is the book minus QQQ; both are null when QQQ was not measured over the book's own window (never over different dates). income is a separate figure beside the returns: the book's income received (dividends, interest, staking rewards) in USD, trailing 12 months (ttm) and year to date (ytd), each as gross, withholding (tax withheld, already netted into net) and net, in total and by holding; per holding, forwardAnnualUsd is the stored dividend yield times the position's value (an estimate, null without a yield), yieldOnCost is trailing-12-month net income over the verified cost basis (null without one) and nextExDividend is the next ex-dividend day. The book's forwardAnnualUsd and yieldOnCost are null when no holding gives the input, never zero; unvaluedReceipts counts income rows left out for want of a USD value. Income is not part of any period's return and nothing combines it into a score.")]
    public async Task<BookPerformanceResult> ExecuteAsync(
        [Description("Lookback windows to compute. Valid values: OneWeek, OneMonth, ThreeMonths, OneYear. Omit for all four.")] IReadOnlyList<BookPerformancePeriod>? periods = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return BookPerformanceResult.Empty(DateOnly.FromDateTime(DateTime.UtcNow));
        }

        var requested = periods is { Count: > 0 }
            ? periods
            : AllPeriods;

        var result = await performance.GetAsync(effective.Value, requested, cancellationToken);
        return result with { Income = await ReadIncomeAsync(effective.Value, cancellationToken) };
    }

    private async Task<BookIncome?> ReadIncomeAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            return await income.GetAsync(userId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Book income unavailable; returning the book returns without it");
            return null;
        }
    }

    private static readonly IReadOnlyList<BookPerformancePeriod> AllPeriods =
    [
        BookPerformancePeriod.OneWeek,
        BookPerformancePeriod.OneMonth,
        BookPerformancePeriod.ThreeMonths,
        BookPerformancePeriod.OneYear,
    ];
}
