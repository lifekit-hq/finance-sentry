namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Published read port (#454 S2): the funding goals and the contribution plan on the person's current Investment
/// Policy Statement - their single home - for cross-module readers such as Wealth, which tracks them against net
/// worth. Implemented inside Research, so Wealth never references it. Callers run with the person in scope.
/// </summary>
public interface IGoalsReader
{
    /// <summary>The current IPS's goals and contribution plan, or null when the person has no current IPS.</summary>
    Task<IpsGoals?> GetCurrentAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>What the IPS says the money is for: its goals, in the order they were written, and the planned contribution.</summary>
public sealed record IpsGoals(IReadOnlyList<GoalTarget> Goals, GoalContributionPlan? ContributionPlan);

/// <summary>
/// One funding goal as the IPS holds it. <paramref name="TargetAmountUsd"/> and <paramref name="TargetDate"/> are
/// null when the owner has not set them; <paramref name="Priority"/> is the owner's own wording.
/// </summary>
public sealed record GoalTarget(string Label, decimal? TargetAmountUsd, DateOnly? TargetDate, string Priority);

/// <summary>The IPS's recurring contribution: <paramref name="AmountUsd"/> every <paramref name="Cadence"/> (free text, e.g. "monthly").</summary>
public sealed record GoalContributionPlan(decimal AmountUsd, string Cadence);
