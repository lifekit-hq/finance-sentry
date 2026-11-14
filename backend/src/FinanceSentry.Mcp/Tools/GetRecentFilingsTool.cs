using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetRecentFilingsTool(
    IQueryHandler<GetRecentFilingsQuery, IReadOnlyList<EdgarFilingDto>> handler)
{
    private const int DefaultLimit = 10;

    [McpServerTool(Name = "get_recent_filings")]
    [Description("Recent SEC EDGAR filings for a US-listed ticker (live from data.sec.gov, no key), newest first, each with a direct documentUrl. Defaults to the material forms 10-K (annual), 10-Q (quarterly), 8-K (material events); pass formTypes to widen or narrow. Use this to detect \"the report just landed\" and to fetch the document to read. Returns empty for non-US/non-EDGAR tickers (e.g. crypto). An EDGAR infrastructure failure is an error, never an empty list: the error text carries a code - [map_unavailable] (the SEC ticker-to-CIK map could not be loaded, so no ticker resolves) or [source_unavailable] (the EDGAR feed failed) - retry later and do not read it as no filings.")]
    public async Task<IReadOnlyList<EdgarFilingDto>> ExecuteAsync(
        [Description("Ticker symbol, e.g. AAPL, NVDA.")] string ticker,
        [Description("Optional SEC form types to include (e.g. [\"10-K\",\"10-Q\",\"8-K\"]). Defaults to 10-K/10-Q/8-K.")] IReadOnlyList<string>? formTypes = null,
        [Description("Max filings to return, default 10.")] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        return await handler.Handle(new GetRecentFilingsQuery(ticker, formTypes, limit), cancellationToken);
    }
}
