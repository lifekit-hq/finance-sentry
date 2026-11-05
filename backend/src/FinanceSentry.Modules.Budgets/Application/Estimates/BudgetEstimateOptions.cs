namespace FinanceSentry.Modules.Budgets.Application.Estimates;

/// <summary>
/// Settings of the monthly estimates, bound from the <c>Budgets:Estimates</c> section.
/// </summary>
public sealed class BudgetEstimateOptions
{
    public const string SectionName = "Budgets:Estimates";

    /// <summary>
    /// "yyyy-MM" months left out of every person's estimate. Empty by default: a month that cannot be trusted is
    /// found per person from their coverage holes (<c>ICoverageHoleReader</c>), because a list here applies to every
    /// user of the deployment. Configure it only for a gap that hits all of them.
    /// </summary>
    public string[] ExcludedMonths { get; set; } = [];

    /// <summary>Whether the month starting on <paramref name="monthStart"/> is left out of the estimates.</summary>
    public bool IsExcluded(DateOnly monthStart)
        => ExcludedMonths.Contains(monthStart.ToString("yyyy-MM"), StringComparer.Ordinal);
}
