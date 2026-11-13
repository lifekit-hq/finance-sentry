using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ListThesisEventsTool(
    IQueryHandler<ListThesisEventsQuery, IReadOnlyList<ThesisEventDto>> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "list_thesis_events")]
    [Description("Lists the caller's price-stamped thesis/candidate lifecycle events (Created, Broken, Unbroken, Closed, Promoted, Rejected, Expired, Snapshot, Revised, Reverted), optionally filtered to one subject. Each event carries its origin (User or Ledger) and, for a thesis write, an audit payload: prior and new version, rationale, evidence references (a Revised event's id is what revert_thesis_revision takes).")]
    public async Task<IReadOnlyList<ThesisEventDto>> ExecuteAsync(
        [Description("Optional thesis/candidate id to filter to a single subject's event trail.")] Guid? subjectId = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return [];
        }

        return await handler.Handle(new ListThesisEventsQuery(effective.Value, subjectId), cancellationToken);
    }
}
