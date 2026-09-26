using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Risk.API.Responses;
using FinanceSentry.Modules.Risk.Application.Commands;
using FinanceSentry.Modules.Risk.Application.Queries;
using FinanceSentry.Modules.Risk.Domain;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class CheckRiskRulesTool(
    IQueryHandler<CheckRiskRulesQuery, CheckRiskRulesResult> queryHandler,
    ICommandHandler<RecordRiskOverrideCommand, Unit> overrideHandler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "check_risk_rules")]
    [Description("With no ticker/amount, returns the current compliance report against the RiskRuleSet: every open violation (rule/observed/limit/excess) plus its acknowledgement state — isAcknowledged, the acknowledged worseningStepPct, hasWorsenedPastStep, and a reportable flag. A violation is acknowledged-but-not-worsened (reportable=false) exactly when it should stay silent; do not re-report it — the flag is authoritative, no need to recall prior turns. Acknowledged violations still appear in this list (the policy view never hides them), just with reportable=false. With ticker+proposedUsd, returns an Allowed|Refused verdict for that proposed position — the hard gate 019's promote_candidate calls before proposing a position. A Refused verdict names the rule and the max compliant size. Pass overrideRefusal=true to proceed anyway on a Refused verdict — the override itself is ALWAYS recorded as a signal + Alert, never silent (FR-007).")]
    public async Task<CheckRiskRulesResponseDto?> ExecuteAsync(
        [Description("Ticker of a proposed position. Omit for the no-arg compliance report.")] string? ticker = null,
        [Description("Proposed USD size to add to the position. Required when ticker is set.")] decimal? proposedUsd = null,
        [Description("If true and the verdict would be Refused, proceed anyway; the override is recorded as a signal and Alert, never silently applied.")] bool overrideRefusal = false,
        [Description("Optional user GUID. Defaults to the authenticated MCP identity.")] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var effective = userId ?? identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        var proposal = ticker is null || proposedUsd is null
            ? null
            : new RiskProposal(ticker, proposedUsd.Value, overrideRefusal);

        var result = await queryHandler.Handle(new CheckRiskRulesQuery(effective.Value, proposal), cancellationToken);

        if (result.Report is not null)
        {
            return CheckRiskRulesResponseDto.FromReport(result.Report, result.Context);
        }

        var verdict = result.Verdict!;
        if (verdict.Decision == RiskDecision.Refused && overrideRefusal && verdict.RuleKey is not null)
        {
            await overrideHandler.Handle(
                new RecordRiskOverrideCommand(
                    effective.Value, verdict.RuleKey, ticker!, verdict.ObservedValue ?? 0m, verdict.LimitValue ?? 0m),
                cancellationToken);

            verdict = verdict with { Decision = RiskDecision.Allowed, Note = $"Overridden: {verdict.Note}" };
        }

        return CheckRiskRulesResponseDto.FromVerdict(verdict, result.HasRuleSet);
    }
}
