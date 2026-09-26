using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class PromoteCandidateTool(
    ICommandHandler<PromoteCandidateCommand, PromoteCandidateResult> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "promote_candidate")]
    [Description("Promotes an active candidate into a monitored InvestmentThesis. Always runs the 022 risk gate (check_risk_rules) first: if the proposed position is Refused, no thesis is created and the verdict (named rule + max compliant size) is returned — unless overrideRisk is true, which records the override as a signal. Set paper=true for a tracking-only promotion that will never draw down real cash: the gate skips the real-book cash-funding rule (MinCashBuffer) but still enforces concentration/sizing rules (MaxPositionWeight, MaxNewPosition, Turnover). Real (non-paper) promotions keep today's full rule set. Invalidation triggers default to the deterministic prefill from the candidate's fundamentals; pass triggers to override. Returns the thesis id (when created) and the gate verdict.")]
    public async Task<PromoteCandidateResult?> ExecuteAsync(
        [Description("Candidate id to promote.")] Guid id,
        [Description("Optional invalidation triggers to override the deterministic prefill.")] IReadOnlyList<ThesisInvalidationTrigger>? triggers = null,
        [Description("Set true to promote despite a Refused risk verdict (records an explicit override signal).")] bool overrideRisk = false,
        [Description("Proposed position size in USD for the risk gate's concentration/sizing check. Defaults to 0 (no sizing).")] decimal proposedUsd = 0m,
        [Description("Optional contemporaneous reasoning captured at promotion time.")] string? decisionNote = null,
        [Description("Optional user GUID. Defaults to the authenticated MCP identity.")] Guid? userId = null,
        [Description("Set true for a paper/tracking-only promotion: the risk gate skips the real-book cash-funding rule (MinCashBuffer) but still enforces concentration/sizing rules. Defaults to false (real-book rule set unchanged).")] bool paper = false,
        CancellationToken cancellationToken = default)
    {
        var effective = userId ?? identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(
            new PromoteCandidateCommand(effective.Value, id, triggers, overrideRisk, proposedUsd, decisionNote, paper),
            cancellationToken);
    }
}
