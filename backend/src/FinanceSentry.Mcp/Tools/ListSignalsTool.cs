using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Radar.Application.Queries;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ListSignalsTool(
    IQueryHandler<ListSignalsQuery, IReadOnlyList<RadarSignalDto>> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "list_signals")]
    [Description("Lists signals from the shared radar_signals log. Filters (scanner, type, subject, since) are ANDed; since defaults to today. Each signal carries timestamp, scanner, type, severity (info|notable|alerted), subject, dedup key, and a JSON evidence payload.")]
    public async Task<IReadOnlyList<RadarSignalDto>> ExecuteAsync(
        [Description("Only signals on/after this date (UTC). Defaults to today.")] DateOnly? since = null,
        [Description("Filter by scanner key, e.g. market_structure.")] string? scanner = null,
        [Description("Filter by signal type, e.g. unusual_move.")] string? type = null,
        [Description("Filter by subject, e.g. a ticker or sector key.")] string? subject = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        return await handler.Handle(
            new ListSignalsQuery(since, scanner, type, subject, effective), cancellationToken);
    }
}
