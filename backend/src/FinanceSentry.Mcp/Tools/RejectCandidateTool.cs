using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.Application.Commands;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class RejectCandidateTool(
    ICommandHandler<RejectCandidateCommand, RejectCandidateResult> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "reject_candidate")]
    [Description("Rejects an active candidate with a reason. The candidate and its scorecard are kept (never deleted) so they remain queryable for the track record and counterfactuals. Records a Rejected lifecycle event.")]
    public async Task<RejectCandidateResult?> ExecuteAsync(
        [Description("Candidate id to reject.")] Guid id,
        [Description("Why the candidate is being rejected.")] string reason,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(
            new RejectCandidateCommand(effective.Value, id, reason), cancellationToken);
    }
}
