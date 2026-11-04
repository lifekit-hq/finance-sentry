namespace FinanceSentry.Modules.Budgets.Application.Estimates;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;

/// <summary>Where a line's suggested amount comes from.</summary>
public static class EstimateBasis
{
    /// <summary>The median of the line's months in the history window.</summary>
    public const string History = "history";

    /// <summary>The detected commitments on the line, at what they cost now.</summary>
    public const string Commitment = "commitment";
}

/// <param name="Amount">USD a month; null when there is neither enough history nor a commitment to suggest one.</param>
/// <param name="Basis"><see cref="EstimateBasis"/>, null with no <paramref name="Amount"/>.</param>
public sealed record LineSuggestion(string Line, decimal? Amount, string? Basis);

/// <summary>
/// The estimate of each line of a month, before any hand edit (<c>BudgetEstimateService</c> lays an override over
/// it). Pure: no I/O, no clock.
/// </summary>
public static class BudgetEstimator
{
    /// <summary>
    /// A suggestion per line that has spend in the history window or an active commitment, keyed by line.
    /// <list type="bullet">
    ///   <item>The statistic is <see cref="Projection.Median"/> over the window's months (a month with no spend in
    ///   the line counts as 0), the same one the projection uses, so one artifact month cannot move it. Below
    ///   <see cref="Projection.MinCompleteMonths"/> months there is no median.</item>
    ///   <item>On a committed line (<see cref="EstimateLines.IsCommitted"/>) the detected commitments replace the
    ///   median when there are any: a rent that stopped, or an installment that ended, no longer counts, and one
    ///   whose price changed counts at the new price. A committed line whose commitments all ended before the month
    ///   gets no suggestion, not the median, which still holds the payments that stopped; a committed line that
    ///   never had a commitment keeps the median.</item>
    ///   <item>On any other line the commitments are a floor: a subscription cannot cost less than it charges, but
    ///   the discretionary spend beside it stays in the median.</item>
    /// </list>
    /// </summary>
    /// <param name="endedCommitments">Commitments that stopped, so a committed line they sat on is not left to the median.</param>
    /// <param name="month">First day of the month the estimate is for; a commitment whose end date passed before it is dropped.</param>
    public static IReadOnlyDictionary<string, LineSuggestion> Suggest(
        EstimateHistory history,
        IReadOnlyList<CategoryMonthSpend> rows,
        IReadOnlyList<CommitmentOnLine> commitments,
        IReadOnlyList<CommitmentOnLine> endedCommitments,
        DateOnly month)
    {
        var committedByLine = commitments
            .Where(c => EstimateLines.IsEstimable(c.Line) && (c.EndDate is null || c.EndDate >= month))
            .GroupBy(c => c.Line, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.MonthlyUsd), StringComparer.Ordinal);

        var endedLines = commitments
            .Where(c => c.EndDate < month)
            .Concat(endedCommitments)
            .Select(c => c.Line)
            .Where(EstimateLines.IsEstimable)
            .ToHashSet(StringComparer.Ordinal);

        var used = history.UsedMonths.ToHashSet(StringComparer.Ordinal);
        var spendByLineMonth = rows
            .Where(r => EstimateLines.IsEstimable(r.Line) && used.Contains(r.Month))
            .GroupBy(r => r.Line, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r.Month, StringComparer.Ordinal)
                    .ToDictionary(m => m.Key, m => m.Sum(r => r.SpendUsd), StringComparer.Ordinal),
                StringComparer.Ordinal);

        var suggestions = new Dictionary<string, LineSuggestion>(StringComparer.Ordinal);
        foreach (var line in spendByLineMonth.Keys.Union(committedByLine.Keys, StringComparer.Ordinal))
        {
            decimal? median = null;
            if (history.IsEnough)
            {
                var byMonth = spendByLineMonth.GetValueOrDefault(line);
                median = Projection.Median(history.UsedMonths.Select(m => byMonth?.GetValueOrDefault(m) ?? 0m));
            }

            var committed = committedByLine.GetValueOrDefault(line);
            if (committed == 0m && endedLines.Contains(line) && EstimateLines.IsCommitted(line))
            {
                suggestions[line] = new LineSuggestion(line, null, null);
                continue;
            }

            suggestions[line] = Choose(line, median, committed);
        }

        return suggestions;
    }

    private static LineSuggestion Choose(string line, decimal? median, decimal committed)
    {
        if (committed > 0m && (EstimateLines.IsCommitted(line) || median is null || committed > median))
            return new LineSuggestion(line, committed, EstimateBasis.Commitment);

        return median is { } m
            ? new LineSuggestion(line, m, EstimateBasis.History)
            : new LineSuggestion(line, null, null);
    }

    /// <summary>
    /// Where a line is expected to finish the month. A past month finished at what was spent. In a month still to
    /// come, or in progress before day <see cref="BudgetPace.WindowStartDay"/>, it is what was spent plus what is
    /// still due of the line's amount. From that day on a discretionary line whose pace
    /// (<see cref="BudgetPace.Calculate"/>) already beats its amount finishes at the projected spend instead; a
    /// committed line never does (see <see cref="EstimateLines.IsCommitted"/>).
    /// </summary>
    public static decimal ExpectedMonthEnd(
        string line, decimal? amount, decimal spent, MonthPhase phase, DateTime asOf)
    {
        if (phase == MonthPhase.Past || amount is not { } expected)
            return spent;

        var stillDue = Math.Max(0m, expected - spent);
        if (phase == MonthPhase.Future
            || EstimateLines.IsCommitted(line)
            || asOf.Day < BudgetPace.WindowStartDay)
            return spent + stillDue;

        var projected = BudgetPace.Calculate(spent, expected, asOf).ProjectedMonthEndSpend;
        return projected > expected ? projected : spent + stillDue;
    }
}

/// <summary>Where a month sits against the clock.</summary>
public enum MonthPhase
{
    Past,
    Current,
    Future,
}
