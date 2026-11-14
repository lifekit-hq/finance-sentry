using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetCompsValuationTool(
    IQueryHandler<GetCompsValuationQuery, CompsValuationResult> handler)
{
    [McpServerTool(Name = "get_comps_valuation")]
    [Description("Trading comps for one EQUITY ticker: where its multiples (EV/EBITDA, EV/Revenue, trailing P/E, forward P/E; P/B for banks and insurers) sit inside a peer set, and the per-share value the peer 25th percentile, median and 75th percentile would imply. Peer set = get_valuation_snapshot's (Yahoo similar symbols, at most 6, or the peers override); a peer that is not an equity is dropped, a peer in another industry is flagged. All figures are trailing-twelve-month from one Yahoo quoteSummary call per ticker, computed on read (nothing stored, no LLM). A peer with negative EBITDA or earnings is left out of that multiple only; a P/E above 100 is an outlier and stays out of the statistics. Statistics are min / p25 / median / p75 / max (median, never the mean; linear-interpolated quartiles); position is the target's percentile rank among usable peers (0-100, ties count half), shown next to its growth and EBITDA-margin rank. Fewer than 3 usable peers makes a multiple notEvaluable with a reason. Banks and insurers: EV multiples are notEvaluable (FINANCIAL_SECTOR), P/E and P/B run. A negative-EBITDA target falls back to EV/Revenue (flag EV_REVENUE_FALLBACK). Implied values bridge EV through debt less cash and shares; a missing piece leaves the value null (flags NET_DEBT_UNAVAILABLE / FX_UNKNOWN), SHARES_FROM_MARKET_CAP and FX_CONVERTED mark the approximations. Missing data is null with a reason — NEVER zero-filled. Reply from summary and flags; multiples and the per-peer exclusions are for follow-up questions. Growth is Yahoo's latest-quarter year-over-year revenue growth; growth and margin are percentages. Non-equity tickers (crypto) return notApplicable=true.")]
    public async Task<CompsValuationResult> ExecuteAsync(
        [Description("Equity ticker (e.g. MCD). Crypto returns an explicit not-applicable result.")] string ticker,
        [Description("Optional peer tickers to override the default sector/industry peer set (at most 6 are used).")] string[]? peers = null,
        CancellationToken cancellationToken = default)
    {
        var peerList = peers is { Length: > 0 } ? peers : null;
        return await handler.Handle(new GetCompsValuationQuery(ticker, peerList), cancellationToken);
    }
}
