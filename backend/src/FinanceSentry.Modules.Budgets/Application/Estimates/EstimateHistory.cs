namespace FinanceSentry.Modules.Budgets.Application.Estimates;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;

/// <summary>Why a month inside the lookback was left out of an estimate (the screen words each in plain terms).</summary>
public static class SkipReasons
{
    /// <summary>The month is on the configured exclusion list (<see cref="BudgetEstimateOptions.ExcludedMonths"/>).</summary>
    public const string Excluded = "excluded";

    /// <summary>Spending stopped reaching the app that month while the account kept syncing (a coverage hole).</summary>
    public const string CoverageGap = "coverage-gap";
}

/// <param name="Month">"yyyy-MM".</param>
public sealed record SkippedMonth(string Month, string Reason);

/// <summary>
/// The months an estimate is learned from: the <see cref="Projection.LookbackMonths"/> complete months before the
/// month it is for, less the ones that cannot be trusted. A month left out is skipped, never counted as zero: a quiet
/// month the app could not see is not a frugal one.
/// </summary>
/// <param name="UsedMonths">"yyyy-MM", oldest first.</param>
/// <param name="Skipped">The months dropped for a reason worth telling the person; months before their first data are not listed.</param>
public sealed record EstimateHistory(IReadOnlyList<string> UsedMonths, IReadOnlyList<SkippedMonth> Skipped)
{
    /// <summary>Below <see cref="Projection.MinCompleteMonths"/> a median is noise, so there is no history estimate.</summary>
    public bool IsEnough => UsedMonths.Count >= Projection.MinCompleteMonths;

    /// <summary>
    /// Resolves the window for <paramref name="month"/> (first day). A month still in progress or yet to come learns
    /// from the six complete months before the current one; a past month from the six before itself.
    /// </summary>
    /// <param name="currentMonth">First day of the month the clock is in.</param>
    /// <param name="firstDataDay">The user's first transaction of any kind; months before it are "no data", not zero.</param>
    /// <param name="holes">Coverage holes overlapping the window; a month any of them touches is skipped.</param>
    public static EstimateHistory Resolve(
        DateOnly month,
        DateOnly currentMonth,
        DateOnly? firstDataDay,
        BudgetEstimateOptions options,
        IReadOnlyList<CoverageHoleSpan> holes)
    {
        var anchor = month < currentMonth ? month : currentMonth;
        var firstDataMonth = firstDataDay is { } day ? new DateOnly(day.Year, day.Month, 1) : (DateOnly?)null;

        var used = new List<string>();
        var skipped = new List<SkippedMonth>();
        for (var back = Projection.LookbackMonths; back >= 1; back--)
        {
            var start = anchor.AddMonths(-back);
            if (firstDataMonth is null || start < firstDataMonth)
                continue;

            var key = start.ToString("yyyy-MM");
            var end = start.AddMonths(1).AddDays(-1);
            if (options.IsExcluded(start))
                skipped.Add(new SkippedMonth(key, SkipReasons.Excluded));
            else if (holes.Any(h => h.Overlaps(start, end)))
                skipped.Add(new SkippedMonth(key, SkipReasons.CoverageGap));
            else
                used.Add(key);
        }

        return new EstimateHistory(used, skipped);
    }
}
