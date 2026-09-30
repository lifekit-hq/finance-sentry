namespace FinanceSentry.Modules.Risk.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Risk.API.Responses;
using FinanceSentry.Modules.Risk.Application.Commands;
using FinanceSentry.Modules.Risk.Application.Queries;
using FinanceSentry.Modules.Risk.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Risk rules surface (022): read/save the versioned rule set, read the current compliance report,
/// run a promotion-time proposal check, and acknowledge a pre-existing violation. All endpoints are
/// scoped to the authenticated user. Rule VALUES are the user's decisions — nothing is defaulted.
/// </summary>
[ApiController]
[Authorize]
[Route("risk")]
public class RiskController(
    IQueryHandler<GetRiskRuleSetQuery, RiskRuleSetDto?> getRules,
    ICommandHandler<SaveRiskRuleSetCommand, RiskRuleSetDto> saveRules,
    IQueryHandler<CheckRiskRulesQuery, CheckRiskRulesResult> checkRules,
    ICommandHandler<AcknowledgeViolationCommand, Unit> acknowledge,
    ICommandHandler<RecordRiskOverrideCommand, Unit> recordOverride) : ControllerBase
{
    [HttpGet("rules")]
    public async Task<IActionResult> GetRules(CancellationToken ct)
    {
        var rules = await getRules.Handle(new GetRiskRuleSetQuery(User.RequireUserId()), ct);
        return Ok(rules);
    }

    [HttpPut("rules")]
    public async Task<IActionResult> SaveRules([FromBody] SaveRiskRulesRequest body, CancellationToken ct)
    {
        try
        {
            var saved = await saveRules.Handle(
                new SaveRiskRuleSetCommand(
                    User.RequireUserId(),
                    body.MaxPositionWeightPct,
                    body.MaxSleeveWeightPct,
                    body.MinCashBufferPct,
                    body.MaxLossPerThesisPct,
                    body.MaxNewPositionPct,
                    body.TurnoverBudgetPerQuarter),
                ct);
            return Ok(saved);
        }
        catch (RiskRuleSetValidationException ex)
        {
            return BadRequest(new { error = ex.Message, errorCode = "INVALID_RISK_RULE" });
        }
    }

    [HttpGet("compliance")]
    public async Task<IActionResult> GetCompliance(CancellationToken ct)
    {
        var result = await checkRules.Handle(new CheckRiskRulesQuery(User.RequireUserId()), ct);
        return Ok(CheckRiskRulesResponseDto.FromReport(result.Report!, result.Context));
    }

    [HttpPost("compliance/check")]
    public async Task<IActionResult> CheckProposal([FromBody] CheckProposalRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Ticker))
        {
            return BadRequest(new { error = "Ticker is required.", errorCode = "MISSING_TICKER" });
        }

        var userId = User.RequireUserId();
        var result = await checkRules.Handle(
            new CheckRiskRulesQuery(userId, new RiskProposal(body.Ticker, body.ProposedUsd, body.Override)),
            ct);

        var verdict = result.Verdict!;
        if (verdict.Decision == RiskDecision.Refused && body.Override && verdict.RuleKey is not null)
        {
            await recordOverride.Handle(
                new RecordRiskOverrideCommand(
                    userId, verdict.RuleKey, body.Ticker, verdict.ObservedValue ?? 0m, verdict.LimitValue ?? 0m),
                ct);
            verdict = verdict with { Decision = RiskDecision.Allowed, Note = $"Overridden: {verdict.Note}" };
        }

        return Ok(CheckRiskRulesResponseDto.FromVerdict(verdict, result.HasRuleSet));
    }

    [HttpPost("violations/acknowledge")]
    public async Task<IActionResult> Acknowledge([FromBody] AcknowledgeViolationRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.RuleKey) || string.IsNullOrWhiteSpace(body.Subject))
        {
            return BadRequest(new { error = "RuleKey and Subject are required.", errorCode = "MISSING_VIOLATION_IDENTITY" });
        }

        await acknowledge.Handle(
            new AcknowledgeViolationCommand(
                User.RequireUserId(),
                body.RuleKey,
                body.Subject,
                body.RemediationNote ?? string.Empty,
                body.ObservedAtAck,
                body.WorseningStepPct),
            ct);
        return NoContent();
    }
}

public record SaveRiskRulesRequest(
    decimal? MaxPositionWeightPct,
    decimal? MaxSleeveWeightPct,
    decimal? MinCashBufferPct,
    decimal? MaxLossPerThesisPct,
    decimal? MaxNewPositionPct,
    int? TurnoverBudgetPerQuarter);

public record CheckProposalRequest(string Ticker, decimal ProposedUsd, bool Override = false);

public record AcknowledgeViolationRequest(
    string RuleKey,
    string Subject,
    string? RemediationNote,
    decimal ObservedAtAck,
    decimal WorseningStepPct);
