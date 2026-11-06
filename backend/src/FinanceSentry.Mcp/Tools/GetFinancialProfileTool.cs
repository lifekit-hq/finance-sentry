using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Mcp.Auditing;
using FinanceSentry.Modules.Wealth.Application.Queries;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetFinancialProfileTool(
    IQueryHandler<GetFinancialProfileQuery, FinancialProfileResponse> profileHandler,
    IIdentityResolver identity,
    ILogger<GetFinancialProfileTool> logger)
{
    private readonly IQueryHandler<GetFinancialProfileQuery, FinancialProfileResponse> _profileHandler = profileHandler;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetFinancialProfileTool> _logger = logger;

    // The profile aggregates declared income, goals and career events, so every call is on the audit trail before it runs.
    [SensitiveProfile("Amount", "Cadence", "GrossOrNet", "Kind", "Date")]
    [McpServerTool(Name = "get_financial_profile")]
    [Description("Returns ONE map of the person's money situation as of asOf, so you can say what you know and what you do not instead of composing five tools and guessing the gaps. Six sections, each {dataQuality, reasons, data}: cashflow (get_cashflow_profile: burn, income, pooled savings rate, runway), goals (get_goals_progress: each IPS goal's standing - onTrack, monthsLeft - the nearest dated goal, the contribution plan against measured savings; no ranges, call get_goals_progress for those), income (get_income_profile: declared monthly income against observed, variance; no source labels or amounts), career (get_career_timeline: how many events, how many shape the scenario; no labels or amounts), disposalTax (get_disposal_tax_context across the held positions: how many have an estimate, how many are withheld and why, the shown estimates added before the CGT annual exemption, EUR) and risk (the IPS risk fields: riskTolerance, riskCapacity, maxDrawdownTolerancePct, riskMeasuredAt, remeasurementDue, primaryHorizonYears, emergencyCushionUsd). dataQuality is Complete (all there), Partial (some - reasons says what is not) or Missing (nothing usable - the section's data may still carry the empty answer it was read as); missingSections names the Missing ones. When a section is Partial or Missing, say what is missing and do not fill it in: a person with no declared income has not told you their income, and 'income unreadable on this host' (DECLARED_UNREADABLE, CAREER_UNREADABLE: the MCP host holds no encryption key, so labels and amounts of declared income and career events are never returned here) is not 'no income'. capacityIndicator is a SUGGESTED risk-capacity band (1-5) for the quarterly re-measurement - a pure function of runway, spending against income, income steadiness (the range of the observed months over the median), whether income that landed is below what was declared, and the nearest dated goal, with every input it read in factors (each with the highest band it allows; the suggestion is the lowest) and what it could not read in missing; status Insufficient means no band is suggested (the runway is unknown). It sits beside recordedCapacity, the number on the IPS now, and NEVER writes the IPS: show it to the owner when get_policy_review reports riskRemeasurementDue or they ask to re-measure, then record the number THEY say with record_risk_remeasurement. It reads capacity (what they can afford to lose), never tolerance (what they would sit through): ask for the tolerance, do not infer it. Reading this is recorded on the person's audit trail (tool name, field names, who asked - never values). All amounts are USD except disposalTax's estimate (EUR). These figures are context for the person's own planning, never a trading signal or advice: do not turn them into buy, sell or hold calls.")]
    public async Task<FinancialProfileResponse?> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var effective = _identity.GetUserId();
        if (effective is null) return null;
        var userIdVal = effective.Value;

        try
        {
            return await _profileHandler.Handle(new GetFinancialProfileQuery(userIdVal), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Financial profile query unavailable for user {UserId}; returning null.", userIdVal);
            return null;
        }
    }
}
