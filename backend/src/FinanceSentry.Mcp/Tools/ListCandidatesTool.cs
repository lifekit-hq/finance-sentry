using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ListCandidatesTool(
    IQueryHandler<ListCandidatesQuery, IReadOnlyList<CandidateListItem>> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "list_candidates")]
    [Description("Lists opportunity candidates with their latest score and lifecycle status. Rejected and expired candidates remain listed for the track record. Optionally filter by status (Active|Promoted|Rejected|Expired) or source (User|Scan).")]
    public async Task<IReadOnlyList<CandidateListItem>> ExecuteAsync(
        [Description("Optional status filter: Active, Promoted, Rejected, or Expired.")] CandidateStatus? status = null,
        [Description("Optional source filter: User or Scan.")] CandidateSource? source = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return [];
        }

        return await handler.Handle(
            new ListCandidatesQuery(effective.Value, status, source), cancellationToken);
    }
}
