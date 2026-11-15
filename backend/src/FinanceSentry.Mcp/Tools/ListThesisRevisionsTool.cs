using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ListThesisRevisionsTool(
    IQueryHandler<GetThesisRevisionsQuery, ThesisRevisionsResult> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "list_thesis_revisions")]
    [Description("The thesis-revision queue: proposals still waiting for the owner (oldest first), and those closed in the last 30 days newest first - status Confirmed (with resultThesisId), Rejected (with the owner's reason, the counterfactual), Expired or Superseded. Read it before propose_thesis_revision, and when a thesis you proposed a change to looks unchanged: it is waiting for the owner, not lost.")]
    public async Task<ThesisRevisionsResult?> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetThesisRevisionsQuery(effective.Value), cancellationToken);
    }
}
