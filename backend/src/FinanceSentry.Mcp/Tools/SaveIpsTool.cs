using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class SaveIpsTool(
    ICommandHandler<SaveIpsCommand, IpsDto> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "save_ips")]
    [Description("Creates a new version of the user's Investment Policy Statement (the written strategy). Each save supersedes the prior version and retains history — amend, don't overwrite. Co-author this from the onboarding interview: only pass values the user has actually confirmed, never invented ones. rebalancingRule defaults to the industry-standard 5/25 rule (rebalance at 5 absolute points or 25% relative drift, annual cadence, new contributions directed to underweight sleeves first) when omitted.")]
    public async Task<IpsDto?> ExecuteAsync(
        [Description("Funding goals — what the money is for and when it's needed.")] IReadOnlyList<InvestmentGoal> goals,
        [Description("Primary time horizon in years.")] int primaryHorizonYears,
        [Description("Behavioral risk tolerance: 1 (very conservative) to 5 (very aggressive).")] int riskTolerance,
        [Description("Target allocation per asset class, each with min/max rebalancing bands.")] IReadOnlyList<AllocationTarget> allocationTargets,
        [Description("Asset classes or tickers the user refuses to hold.")] IReadOnlyList<string> exclusions,
        [Description("Emergency cash cushion in USD, if any.")] decimal? emergencyCushionUsd = null,
        [Description("Risk capacity (ability to afford risk): 1 to 5, derived from horizon + income stability.")] int? riskCapacity = null,
        [Description("Maximum drawdown the user says they can tolerate, as a percent.")] decimal? maxDrawdownTolerancePct = null,
        [Description("Rebalancing rule. Omit to use the 5/25 default.")] RebalancingRule? rebalancingRule = null,
        [Description("Recurring contribution plan (dollar-cost averaging), if any.")] ContributionPlan? contributionPlan = null,
        [Description("Free-form sell discipline — the rules for when to trim or exit.")] string? sellDiscipline = null,
        [Description("Mandatory cooling-off period in days before any allocation/fund change. Defaults to 90.")] int? coolingOffDays = null,
        [Description("Review cadence (e.g. annual). Defaults to annual.")] string? reviewCadence = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(
            new SaveIpsCommand(
                effective.Value,
                goals ?? [],
                primaryHorizonYears,
                emergencyCushionUsd,
                riskTolerance,
                riskCapacity,
                maxDrawdownTolerancePct,
                allocationTargets ?? [],
                rebalancingRule,
                contributionPlan,
                sellDiscipline,
                coolingOffDays,
                exclusions ?? [],
                reviewCadence),
            cancellationToken);
    }
}
