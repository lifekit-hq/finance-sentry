using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class RecordRiskRemeasurementTool(
    ICommandHandler<RecordRiskRemeasurementCommand, RiskRemeasurementDto> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "record_risk_remeasurement")]
    [Description("Records a re-measurement of the owner's risk tolerance (1-5), risk capacity (1-5) and maximum tolerated drawdown (percent, above 0 up to 100) as a NEW version of the Investment Policy Statement — the prior version is kept, never edited, and every other policy field carries forward unchanged. Use it when get_policy_review reports riskRemeasurementDue, or on the owner's own initiative. Pass only what the owner actually said: a tolerance is behaviour (what they would do after a fall), capacity is what they can afford, the drawdown is the largest peak-to-trough decline they would sit through — never infer one from their portfolio. The recorded drawdown is enforced by the risk layer: once the book's decline from its peak passes it, check_risk_rules reports a MaxDrawdown violation (subject BOOK) and the usual policy-violation alert fires. The response carries the new version and the field-by-field diff against the version it supersedes. Re-confirming unchanged numbers is valid and still records a fresh measurement.")]
    public async Task<RiskRemeasurementDto?> ExecuteAsync(
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

        return await handler.Handle(
            new RecordRiskRemeasurementCommand(effective.Value, riskTolerance, riskCapacity, maxDrawdownTolerancePct),
            cancellationToken);
    }
}
