using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Risk.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetRiskRulesTool(
    IQueryHandler<GetRiskRuleSetQuery, RiskRuleSetDto?> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "get_risk_rules")]
    [Description("Returns the user's current versioned RiskRuleSet — maxPositionWeightPct, maxSleeveWeightPct, minCashBufferPct, maxLossPerThesisPct, maxNewPositionPct, turnoverBudgetPerQuarter, allocationTargets. Returns null when no rule set exists yet; rule VALUES are Denys's decisions, never inferred or defaulted. Defaults to the authenticated MCP identity.")]
    public async Task<RiskRuleSetDto?> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(new GetRiskRuleSetQuery(effective.Value), cancellationToken);
    }
}
