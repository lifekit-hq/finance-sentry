namespace FinanceSentry.Modules.Risk.Application.Services;

using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Ports;

/// <summary>Pure function: (BookSnapshot, RiskRuleSet, allocation targets, ack state) -&gt; ComplianceReport / RiskVerdict.</summary>
public interface IRiskEvaluationService
{
    ComplianceReport Evaluate(
        BookSnapshot book,
        RiskRuleSet? ruleSet,
        IReadOnlyList<AllocationDriftTarget> allocationTargets,
        IReadOnlyList<PolicyViolationAck> acks,
        DateTimeOffset? now = null);

    RiskVerdict EvaluateProposal(
        BookSnapshot book,
        RiskRuleSet? ruleSet,
        string ticker,
        decimal proposedUsd,
        int turnoverCountThisQuarter,
        bool isPaper = false);
}
