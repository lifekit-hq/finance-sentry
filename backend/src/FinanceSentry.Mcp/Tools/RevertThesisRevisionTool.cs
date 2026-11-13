using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.Application.Commands;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class RevertThesisRevisionTool(
    ICommandHandler<RevertThesisRevisionCommand, RevertThesisRevisionResult> handler,
    IIdentityResolver identity,
    IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool(Name = "revert_thesis_revision")]
    [Description("Undoes a thesis revision by restoring the version it replaced. eventId is the id of the Revised (or Reverted) event from list_thesis_events; its audit holds the prior and new version. Only the latest change can be undone: if the thesis was changed again since, the call is refused (outcome ThesisChangedSince) - revert the later revision first. The reversal is itself journaled (a Reverted event carrying the replaced version, origin User or Ledger, and revertsEventId), so it can be undone the same way.")]
    public async Task<RevertThesisRevisionResult?> ExecuteAsync(
        [Description("Id of the Revised or Reverted thesis event to undo (from list_thesis_events).")] Guid eventId,
        [Description("Why the revision is being reverted; kept in the audit trail.")] string? rationale = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(
            new RevertThesisRevisionCommand(
                effective.Value, eventId, rationale, McpCallerOrigin.Of(httpContextAccessor)),
            cancellationToken);
    }
}
