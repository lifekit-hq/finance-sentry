using System.ComponentModel;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class DecideThesisRevisionTool(
    ICommandHandler<DecideThesisRevisionCommand, DecideThesisRevisionResult> handler,
    IIdentityResolver identity)
{
    [Authorize(Policy = AuthPolicies.RequireMcpPersonalToken)]
    [McpServerTool(Name = "decide_thesis_revision")]
    [Description("Owner only: confirms or rejects a pending thesis revision, and only when the owner has told you their decision in this conversation; never decide on their behalf. Confirm is the single step that writes: a Promote runs the 022 risk gate first (a refusal returns outcome RiskGateRefused with the named rule and leaves the proposal pending; overrideRisk is the owner's explicit call), then creates the thesis; a NarrativeUpdate or TriggerChange saves the thesis; a Close removes it. Each is journaled as the owner's write with the proposal's rationale and evidence. Reject needs the owner's real reason in note, kept as the counterfactual. Returns the decided proposal and the thesis it wrote; errors when it is not pending or has expired.")]
    public async Task<DecideThesisRevisionResult?> ExecuteAsync(
        [Description("The proposal id from list_thesis_revisions.")] Guid id,
        [Description("Confirm or Reject.")] string verdict,
        [Description("Optional note on a confirm; on a reject, the owner's reason (required). Max 2000 characters.")] string? note = null,
        [Description("Promote only: confirm despite a Refused risk verdict (records an explicit override signal). The owner's decision alone.")] bool overrideRisk = false,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        var parsed = Enum.TryParse<ThesisRevisionVerdict>(verdict?.Trim(), ignoreCase: true, out var v) && Enum.IsDefined(v)
            ? v
            : throw new InvalidThesisRevisionException("verdict must be Confirm or Reject.");
        return await handler.Handle(
            new DecideThesisRevisionCommand(effective.Value, id, parsed, note, overrideRisk), cancellationToken);
    }
}
