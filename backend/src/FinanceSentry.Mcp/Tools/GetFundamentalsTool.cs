using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetFundamentalsTool(
    IQueryHandler<GetFundamentalsQuery, FundamentalsDto> handler)
{
    private const int DefaultMaxPerConcept = 5;

    [McpServerTool(Name = "get_fundamentals")]
    [Description("Reported financial fundamentals for a listed company: Revenue, GrossProfit, OperatingIncome, NetIncome, DilutedEPS, StockholdersEquity - the most recent periods, newest first, each tagged with periodEnd / fiscalPeriod / form / taxonomy and sourceProvenance (provider, documentUrl, ingestedAt). "
        + "Read from an ordered provider chain: SEC EDGAR XBRL first (us-gaap 10-K/10-Q; ifrs-full 20-F/40-F for a foreign private issuer such as GRAB), then Yahoo Finance only for a period basis (quarterly or annual) EDGAR cannot supply fresh - e.g. a foreign issuer's quarters, which it only furnishes on 6-K. One basis always comes from one provider, so never mix bases when deriving ratios. Quarterly facts from Yahoo carry fiscalPeriod '3M' (calendar period end, no fiscal quarter label) and an empty form. "
        + "Returns { ticker, coverage, facts }. coverage.status is 'covered', 'no_source_available' (no source has numbers for this ticker), 'unsupported_issuer_type' (an ETF/fund: no company financials) or 'source_unavailable' (a provider failed - retry later); coverage.issuerType says domestic_filer / foreign_private_issuer / investment_fund / not_sec_registrant when known, coverage.bases names the provider behind each basis (stale = no fresher series exists), coverage.reason explains anything missing. Never treat empty facts as zero. "
        + "Values are raw as reported in the stated unit (usually USD, or USD/shares for EPS); derive ratios like gross margin yourself. Use this to evaluate a thesis's invalidation triggers against real numbers after an earnings report.")]
    public async Task<FundamentalsDto> ExecuteAsync(
        [Description("Ticker symbol, e.g. AAPL, NVDA.")] string ticker,
        [Description("Max datapoints per concept, default 5, max 20.")] int maxPerConcept = DefaultMaxPerConcept,
        CancellationToken cancellationToken = default)
    {
        return await handler.Handle(new GetFundamentalsQuery(ticker, maxPerConcept), cancellationToken);
    }
}
