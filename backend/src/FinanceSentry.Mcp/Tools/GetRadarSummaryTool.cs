using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Radar.Application.Queries;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetRadarSummaryTool(
    IQueryHandler<GetRadarSummaryQuery, RadarSummary> handler)
{
    [McpServerTool(Name = "get_radar_summary")]
    [Description("START HERE for any market/narration question. One-call Radar snapshot: today's sector leaders/laggards with rank deltas, universe breadth, and today's notable+ signals, plus a stale flag when computed over stale bars. After this overview, drill down with get_market_structure (one ticker), get_relative_strength, get_sector_rotation, or get_market_breadth. Reads persisted bars only — never triggers ingestion.")]
    public async Task<RadarSummary> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        return await handler.Handle(new GetRadarSummaryQuery(), cancellationToken);
    }
}
