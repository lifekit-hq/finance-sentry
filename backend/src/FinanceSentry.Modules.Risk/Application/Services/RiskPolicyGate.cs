namespace FinanceSentry.Modules.Risk.Application.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Risk.Application.Queries;
using FinanceSentry.Modules.Risk.Domain;

/// <summary>
/// <see cref="IRiskPolicyGate"/> impl: wraps <see cref="CheckRiskRulesQueryHandler"/> so other
/// modules (019's promote_candidate) can run the promotion-time policy check without a compile-time
/// dependency on Risk. When the user has no rule set on file, this never blocks (FR-011b).
/// </summary>
public sealed class RiskPolicyGate(IQueryHandler<CheckRiskRulesQuery, CheckRiskRulesResult> queryHandler)
    : IRiskPolicyGate
{
    private const string NoRuleSetNote = "no rules on file";

    public async Task<RiskGateVerdict> CheckProposalAsync(
        Guid userId, string ticker, decimal proposedUsd, bool overrideFlag, bool isPaper = false, CancellationToken ct = default)
    {
        var proposal = new RiskProposal(ticker, proposedUsd, overrideFlag, isPaper);
        var result = await queryHandler.Handle(new CheckRiskRulesQuery(userId, proposal), ct);

        if (!result.HasRuleSet || result.Verdict is null)
        {
            return new RiskGateVerdict(RiskGateDecision.Allowed, null, null, null, null, NoRuleSetNote);
        }

        var verdict = result.Verdict;
        var decision = verdict.Decision == RiskDecision.Refused
            ? RiskGateDecision.Refused
            : RiskGateDecision.Allowed;

        return new RiskGateVerdict(
            decision,
            verdict.RuleKey,
            verdict.ObservedValue,
            verdict.LimitValue,
            verdict.MaxCompliantSizeUsd,
            verdict.Note);
    }
}
