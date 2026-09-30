using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Radar.Application.Queries;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetMarketStructureTool(
    IQueryHandler<GetMarketStructureQuery, TickerStructure?> handler)
{
    [McpServerTool(Name = "get_market_structure")]
    [Description("Drill-down for one ticker — call get_radar_summary first for the market overview. Returns market-structure metrics for one ticker: returns/RS vs SPY over 21/63/126/252 trading days, MA20/50/200, extension from the 50-day MA, 63-day volatility, today's z-score, volume ratio, and a stale flag. Windows with insufficient history are null (not evaluable). Reads persisted bars only — never triggers ingestion.")]
    public async Task<TickerStructure?> ExecuteAsync(
        [Description("Ticker symbol, e.g. NVDA.")] string ticker,
        CancellationToken cancellationToken = default)
    {
        return await handler.Handle(new GetMarketStructureQuery(ticker), cancellationToken);
    }
}
