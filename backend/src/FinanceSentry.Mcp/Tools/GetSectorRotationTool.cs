using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Radar.Application.Queries;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetSectorRotationTool(
    IQueryHandler<GetSectorRotationQuery, IReadOnlyList<SectorRotationRow>> handler)
{
    [McpServerTool(Name = "get_sector_rotation")]
    [Description("Drill-down — call get_radar_summary first for the market overview (it already includes today's sector leaders/laggards). Ranks the 11 SPDR sector ETFs by relative strength per window and reports each sector's rank plus its rank delta vs 21 trading days prior (rotation). Reads persisted bars only — never triggers ingestion.")]
    public async Task<IReadOnlyList<SectorRotationRow>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        return await handler.Handle(new GetSectorRotationQuery(), cancellationToken);
    }
}
