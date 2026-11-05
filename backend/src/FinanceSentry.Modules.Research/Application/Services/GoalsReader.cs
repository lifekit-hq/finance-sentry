namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;

/// <summary><see cref="IGoalsReader"/> impl over Research's own <see cref="GetIpsQuery"/>.</summary>
public sealed class GoalsReader(IQueryHandler<GetIpsQuery, IpsDto?> getIps) : IGoalsReader
{
    public async Task<IpsGoals?> GetCurrentAsync(Guid userId, CancellationToken ct = default)
    {
        var ips = await getIps.Handle(new GetIpsQuery(userId), ct);
        if (ips is null)
            return null;

        return new IpsGoals(
            [.. ips.Goals.Select(g => new GoalTarget(g.Label, g.TargetAmountUsd, g.TargetDate, g.Priority))],
            ips.ContributionPlan is { } plan ? new GoalContributionPlan(plan.AmountUsd, plan.Cadence) : null);
    }
}
