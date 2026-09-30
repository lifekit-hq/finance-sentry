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
    [Description("Returns time-weighted return (TWR) for the IBKR brokerage portfolio versus the SPY benchmark for one or more lookback windows (1W, 1M, 3M, 1Y). Each period includes bookTwr, spyTwr, delta (book minus SPY), and a verdict (outperform / underperform / inline). Periods with insufficient price history are omitted. Defaults to all four windows and the authenticated MCP identity.")]
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
