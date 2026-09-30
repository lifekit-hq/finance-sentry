using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Risk.Application.Commands;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class AcknowledgeRiskViolationTool(
    ICommandHandler<AcknowledgeViolationCommand, Unit> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "acknowledge_risk_violation")]
    [Description("Acknowledges a pre-existing policy violation with a remediation note (FR-003) — e.g. 'trim DRAM on strength to <=30% by Q4'. After acknowledgement the violation is reported as Acknowledged and re-alerts only if it worsens past the given worsening step, so a known starting-state violation (e.g. an oversized concentrated position) is managed as a tracked remediation instead of nagging daily. Acknowledgement is Denys's deliberate act; the system never auto-acknowledges.")]
    public async Task<bool> ExecuteAsync(
        [Description("The violated rule key, e.g. 'maxPositionWeight'.")] string ruleKey,
        [Description("The subject of the violation, e.g. the ticker 'DRAM' or a sleeve name.")] string subject,
        [Description("Contemporaneous remediation note — the plan to resolve the violation.")] string remediationNote,
        [Description("Observed value at acknowledgement time (e.g. 0.46 for a 46% weight).")] decimal observedAtAck,
        [Description("Worsening step past which a re-alert fires, e.g. 0.05 to re-alert if it worsens by 5pp.")] decimal worseningStepPct,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return false;
        }

        await handler.Handle(
            new AcknowledgeViolationCommand(
                effective.Value,
                ruleKey,
                subject,
                remediationNote,
                observedAtAck,
                worseningStepPct),
            cancellationToken);

        return true;
    }
}
