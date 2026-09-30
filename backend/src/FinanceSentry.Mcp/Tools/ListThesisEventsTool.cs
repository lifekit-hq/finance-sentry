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
    [Description("Lists the caller's price-stamped thesis/candidate lifecycle events (Created, Broken, Unbroken, Closed, Promoted, Rejected, Expired, Snapshot), optionally filtered to one subject.")]
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
