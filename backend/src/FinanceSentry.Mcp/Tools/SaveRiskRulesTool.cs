using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Risk.Application.Commands;
using FinanceSentry.Modules.Risk.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class SaveRiskRulesTool(
    ICommandHandler<SaveRiskRuleSetCommand, RiskRuleSetDto> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "save_risk_rules")]
    [Description("Saves a new version of the user's RiskRuleSet (appends — never mutates a prior version, full audit trail). Validates ranges: weights/percentages must be in (0,1] if set; turnoverBudgetPerQuarter must be non-negative if set. All fields are individually optional — an unset rule is simply not checked, never defaulted. Rule VALUES are Denys's decisions; this tool never invents limits.")]
    public async Task<RiskRuleSetDto?> ExecuteAsync(
        [Description("Max weight (0,1] any single position may hold of the total book, e.g. 0.25 for 25%.")] decimal? maxPositionWeightPct = null,
        [Description("Max weight (0,1] any single sleeve (brokerage/crypto/banking) may hold.")] decimal? maxSleeveWeightPct = null,
        [Description("Min fraction (0,1] of the book that must remain in cash.")] decimal? minCashBufferPct = null,
        [Description("Max loss (0,1] from entry before a thesis's price_drawdown trigger fires by default.")] decimal? maxLossPerThesisPct = null,
        [Description("Max weight (0,1] any single NEW position may be sized to.")] decimal? maxNewPositionPct = null,
        [Description("Max discretionary trades per rolling quarter, non-negative.")] int? turnoverBudgetPerQuarter = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(
            new SaveRiskRuleSetCommand(
                effective.Value,
                maxPositionWeightPct,
                maxSleeveWeightPct,
                minCashBufferPct,
                maxLossPerThesisPct,
                maxNewPositionPct,
                turnoverBudgetPerQuarter),
            cancellationToken);
    }
}
