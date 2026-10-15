using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetScorecardTool(
    IQueryHandler<GetScorecardQuery, ScorecardResult> handler)
{
    [McpServerTool(Name = "get_scorecard")]
    [Description("Scorecard for one EQUITY ticker with its verdict word (STRONG BUY / BUY / WAIT / AVOID, or NOT SCORED). Four axes, never blended into one number: structure (raw 0-100 score and the regime-adjusted score the verdict reads, with RS/return windows, extension from MA50, volume ratio), fundamentals (0-100 or null with reasons; revenue YoY, gross margin level and trend, EPS YoY), crowding (Early / Normal / Extended, null when there is no extension data) and valuation as raw facts (trailing P/E vs its own 5-year average as a premium %, forward P/E, consensus target and implied upside). The verdict carries its rules version, the rule that fired, and the whole ordered rule table with each condition's threshold, the value it read and whether it was met. Computed at read time; nothing is stored and nothing is traded. Non-equity tickers (crypto, funds) return NotScored.")]
    public async Task<ScorecardResult> ExecuteAsync(
        [Description("Equity ticker (e.g. MCD).")] string ticker,
        CancellationToken cancellationToken = default) =>
        await handler.Handle(new GetScorecardQuery(ticker), cancellationToken);
}
