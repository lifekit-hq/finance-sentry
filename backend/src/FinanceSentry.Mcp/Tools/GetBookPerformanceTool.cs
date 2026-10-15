using System.ComponentModel;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetBookPerformanceTool(
    IBookPerformanceService performance,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_book_performance")]
    [Description("Returns the change in invested value (includes deposits; NOT a time-weighted return) for the invested brokerage portfolio (idle broker cash excluded; deposits, withdrawals and purchases from idle cash are not adjusted for, so a contribution reads as a gain) versus the SPY benchmark's adjusted-close change for one or more lookback windows (1W, 1M, 3M, 1Y). Each period includes bookTwr (the book's change in invested value, despite the name), spyTwr (SPY's change), delta (book minus SPY), and a verdict (outperform / underperform / inline). Each period's book is the same book side as a typed headline record: method VALUE_CHANGE, basis START_VALUE, scope invested_brokerage, window, amount (the USD change), percent (bookTwr), quality (COMPLETE / ESTIMATED / DEGRADED / UNAVAILABLE) and typed reasons (STALE_SOURCE, BACKFILLED_ESTIMATE, FX_TODAY_RATE, FX_UNKNOWN_1TO1, SHORT_HISTORY when there is no measurable window, ZERO_BASE when the start value is zero); read quality before quoting bookTwr. Each period also carries the flow-neutral holdings-based return: holdingsBasedReturn (the brokerage book's price return from revaluing each capture's quantities at the next capture's prices and chaining, so deposits and purchases from idle cash read as 0%; excludes dividends) against spyPriceReturn (SPY's change in close, not adjusted close, between the first and last capture the index covers), with holdingsBasedDelta (difference). When there is no number, holdingsBasedUnavailableReason says why (InsufficientCaptures, WindowNotCovered, MissingPrice, NoInvestedBase; never 0) and holdingsBasedMissingPriceSymbols names the positions without a price. Periods with insufficient price or invested-value history are omitted. Defaults to all four windows and the authenticated MCP identity.")]
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

        return await performance.GetAsync(effective.Value, requested, cancellationToken);
    }

    private static readonly IReadOnlyList<BookPerformancePeriod> AllPeriods =
    [
        BookPerformancePeriod.OneWeek,
        BookPerformancePeriod.OneMonth,
        BookPerformancePeriod.ThreeMonths,
        BookPerformancePeriod.OneYear,
    ];
}
