using System.ComponentModel;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Mcp.Auditing;
using FinanceSentry.Mcp.Responses;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Wealth.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class RecordRiskRemeasurementTool(
    ICommandHandler<RecordRiskRemeasurementCommand, RiskRemeasurementDto> handler,
    IQueryHandler<GetFinancialProfileQuery, FinancialProfileResponse> profile,
    IIdentityResolver identity,
    ILogger<RecordRiskRemeasurementTool> logger)
{
    // The capacity indicator reads declared income and career events, so the call is on the audit trail like get_financial_profile.
    [SensitiveProfile("Amount", "Cadence", "GrossOrNet", "Kind", "Date")]
    [Authorize(Policy = AuthPolicies.RequireMcpPersonalToken)]
    [McpServerTool(Name = "record_risk_remeasurement")]
    [Description("Records a re-measurement of the owner's risk tolerance (1-5), risk capacity (1-5) and maximum tolerated drawdown (percent, above 0 up to 100) as a NEW version of the Investment Policy Statement — the prior version is kept, never edited, and every other policy field carries forward unchanged. Use it when get_policy_review reports riskRemeasurementDue, or on the owner's own initiative. Pass only what the owner actually said: a tolerance is behaviour (what they would do after a fall), capacity is what they can afford, the drawdown is the largest peak-to-trough decline they would sit through — never infer one from their portfolio. The recorded drawdown is enforced by the risk layer: once the book's decline from its peak passes it, check_risk_rules reports a MaxDrawdown violation (subject BOOK) and the usual policy-violation alert fires. The response carries the new version and the field-by-field diff against the version it supersedes. Re-confirming unchanged numbers is valid and still records a fresh measurement. The response also carries capacityIndicator: the SUGGESTED capacity band (1-5) the person's runway, spending against income, income steadiness and nearest dated goal point to (see get_financial_profile for its inputs), beside the capacity just recorded - show both and say if they differ, but the number recorded is always the one the owner gave: the indicator never writes the policy statement and never replaces the owner's answer. Capacity is not tolerance: ask for the tolerance, do not infer it from the indicator.")]
    public async Task<RiskRemeasurementToolResult?> ExecuteAsync(
        [Description("Behavioral risk tolerance: 1 (very conservative) to 5 (very aggressive).")] int riskTolerance,
        [Description("Risk capacity (ability to afford risk): 1 to 5.")] int riskCapacity,
        [Description("Largest peak-to-trough decline the owner says they would sit through, as a percent (e.g. 25 for 25%).")] decimal maxDrawdownTolerancePct,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        var indicator = await ReadIndicatorAsync(effective.Value, riskCapacity, cancellationToken);
        var recorded = await handler.Handle(
            new RecordRiskRemeasurementCommand(effective.Value, riskTolerance, riskCapacity, maxDrawdownTolerancePct),
            cancellationToken);

        return new RiskRemeasurementToolResult(recorded.Ips, recorded.Diff, indicator);
    }

    // Read before the write so the suggestion is what the owner would have seen while answering; a failed read never blocks the recording.
    private async Task<CapacitySuggestion?> ReadIndicatorAsync(Guid userId, int recordedCapacity, CancellationToken cancellationToken)
    {
        try
        {
            var read = (await profile.Handle(new GetFinancialProfileQuery(userId), cancellationToken)).CapacityIndicator;
            return read with { RecordedCapacity = recordedCapacity, DiffersFromRecorded = read.SuggestedBand is { } band && band != recordedCapacity };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Capacity indicator unavailable for user {UserId}; recording without it.", userId);
            return null;
        }
    }
}
