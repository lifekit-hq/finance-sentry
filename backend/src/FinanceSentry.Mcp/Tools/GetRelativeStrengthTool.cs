using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Radar.Application.Queries;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetRelativeStrengthTool(
    IQueryHandler<GetRelativeStrengthQuery, IReadOnlyList<TickerStructure>> handler)
{
    [McpServerTool(Name = "get_relative_strength")]
    [Description("Drill-down — call get_radar_summary first for the market overview. Returns relative-strength-vs-SPY structure for a set of tickers (default = the full Radar universe), ordered by 63-day RS descending. Reads persisted bars only — never triggers ingestion.")]
    public async Task<IReadOnlyList<TickerStructure>> ExecuteAsync(
        [Description("Optional tickers. Defaults to the full Radar universe.")] IReadOnlyList<string>? tickers = null,
        CancellationToken cancellationToken = default)
    {
        return await handler.Handle(new GetRelativeStrengthQuery(tickers), cancellationToken);
    }
}
