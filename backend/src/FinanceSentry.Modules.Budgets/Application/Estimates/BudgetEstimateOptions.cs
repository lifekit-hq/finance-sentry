namespace FinanceSentry.Modules.Budgets.Application.Estimates;

/// <summary>
/// Settings of the monthly estimates, bound from the <c>Budgets:Estimates</c> section.
/// </summary>
public sealed class BudgetEstimateOptions
{
    public const string SectionName = "Budgets:Estimates";

    /// <summary>
    /// The months an estimate does not learn from while the spend inside them cannot be trusted: the owner's
    /// day-to-day spending moved to a credit card the bank link does not expose from 2026-08-29 (the Revolut gap,
    /// #1234), so September and October 2026 read as frugal months they were not. Lift it by configuring
    /// <see cref="ExcludedMonths"/> (an empty list excludes nothing) once the card is connected.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultExcludedMonths = ["2026-09", "2026-10"];

    /// <summary>
    /// "yyyy-MM" months left out of every estimate. Null means <see cref="DefaultExcludedMonths"/>; a configured list
    /// replaces it.
    /// </summary>
    public string[]? ExcludedMonths { get; set; }

    /// <summary>Whether the month starting on <paramref name="monthStart"/> is left out of the estimates.</summary>
    public bool IsExcluded(DateOnly monthStart)
        => (ExcludedMonths ?? [.. DefaultExcludedMonths]).Contains(
            monthStart.ToString("yyyy-MM"), StringComparer.Ordinal);
}
