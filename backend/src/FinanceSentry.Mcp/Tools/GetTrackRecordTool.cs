using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetTrackRecordTool(
    IQueryHandler<GetTrackRecordQuery, TrackRecordSummaryDto> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_track_record")]
    [Description("Answers 'is this earning money' in one call: counts, terminal/active hit rate, average/median/best/worst excess return vs SPY, split by candidate source (User/Scan) and status, with a low-sample caveat below 30 closed records.")]
    public async Task<TrackRecordSummaryDto?> ExecuteAsync(
        [Description("Optional filter: 'User' or 'Scan' (Scan is empty until 019 ships).")] string? source = null,
        [Description("Optional filter: 'Active', 'Broken', 'Closed', 'Promoted', 'Rejected', or 'Expired'.")] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetTrackRecordQuery(effective.Value, source, status), cancellationToken);
    }
}
