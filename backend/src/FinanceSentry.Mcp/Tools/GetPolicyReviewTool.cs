using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetPolicyReviewTool(
    IQueryHandler<GetPolicyReviewStatusQuery, PolicyReviewStatusDto> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_policy_review")]
    [Description("Returns the state of the scheduled investment-policy review: the review cadence recorded in the IPS, when it was last reviewed, when the next review is due, and whether it is due, overdue or missed (daysOverdue). Also returns the latest completed review's structured proposal — per-asset-class drift against the IPS bands (Within / OverBand / UnderBand / Unplanned), suggested adjustments (Trim / Add / Review with an approximate USD size and rationale) and the overall rationale. The risk measurement rides the same cadence: riskMeasuredAt, drawdownToleranceSet and riskRemeasurementDue say whether tolerance, capacity and drawdown tolerance are due to be re-measured (record the answer with record_risk_remeasurement). Recommend-only: a review never places, stages or routes an order. When the user has no IPS, HasIps is false. Defaults to the authenticated MCP identity.")]
    public async Task<PolicyReviewStatusDto?> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetPolicyReviewStatusQuery(effective.Value), cancellationToken);
    }
}
