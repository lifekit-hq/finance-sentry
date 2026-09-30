using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Radar.Application.Queries;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetMarketBreadthTool(
    IQueryHandler<GetMarketBreadthQuery, BreadthResult> handler)
{
    [McpServerTool(Name = "get_market_breadth")]
    [Description("Drill-down — call get_radar_summary first for the market overview (it already includes breadth). Returns Radar universe breadth: the percentage of tickers trading above their 20/50/200-day moving averages, plus the evaluated count. Reads persisted bars only — never triggers ingestion.")]
    public async Task<BreadthResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        return await handler.Handle(new GetMarketBreadthQuery(), cancellationToken);
    }
}
