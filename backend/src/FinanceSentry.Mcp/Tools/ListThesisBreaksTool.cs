using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ListThesisBreaksTool(
    IQueryHandler<ListThesisBreaksQuery, IReadOnlyList<ThesisBreakView>> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "list_thesis_breaks")]
    [Description("Lists the caller's currently broken investment theses, with the breached metric, observed value(s)/period(s), threshold, direction, and reason. Read-only — does not re-evaluate.")]
    public async Task<IReadOnlyList<ThesisBreakView>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return [];
        }

        return await handler.Handle(new ListThesisBreaksQuery(effective.Value), cancellationToken);
    }
}
