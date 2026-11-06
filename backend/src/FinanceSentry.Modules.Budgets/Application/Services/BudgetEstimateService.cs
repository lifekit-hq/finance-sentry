namespace FinanceSentry.Modules.Budgets.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Budgets.API.Responses;
using FinanceSentry.Modules.Budgets.Application.Estimates;
using FinanceSentry.Modules.Budgets.Domain;
using Microsoft.Extensions.Options;

/// <summary>
/// Builds a month's estimates (design of record: issue #1231). Each line is the median of the usable months before it
/// (<see cref="BudgetEstimator"/>), a detected commitment where there is one, or a hand-edited override that replaces
/// both until the person resets it. Nothing is stored: the suggestion is recomputed on every read, so it follows the
/// history and the commitments instead of going stale.
/// <para>
/// Spend is the dashboard's honest figure (<see cref="IMonthlyCategorySpendReader"/>). The history it learns from skips
/// the configured exclusion months and every month a coverage hole touches (<see cref="ICoverageHoleReader"/>), and
/// months before the user's first data are no data rather than zero. The month being viewed is checked against the same
/// holes: when one touches it, the response says its spend is incomplete instead of letting a low figure read as thrift.
/// </para>
/// </summary>
public class BudgetEstimateService(
    IMonthlyCategorySpendReader spend,
    ICoverageHoleReader coverageHoles,
    ICategoryNormalizationService normalization,
    IOptions<BudgetEstimateOptions> options) : IBudgetEstimateService
{
    public async Task<BudgetSummaryResponse> ComputeAsync(
        Guid userId, int year, int month, DateTime asOf, IReadOnlyList<Budget> overrides, CancellationToken ct = default)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var currentMonth = new DateOnly(asOf.Year, asOf.Month, 1);
        var phase = monthStart < currentMonth ? MonthPhase.Past
            : monthStart == currentMonth ? MonthPhase.Current
            : MonthPhase.Future;

        // A month in progress reads as of the day; every other month reads as complete, where the pace ratio
        // collapses to the plain spent / amount ratio.
        var paceAsOf = phase == MonthPhase.Current ? asOf.Date : new DateTime(year, month, DateTime.DaysInMonth(year, month));

        var windowStart = (monthStart < currentMonth ? monthStart : currentMonth).AddMonths(-Projection.LookbackMonths);
        var series = await spend.GetAsync(userId, windowStart, monthEnd, ct);
        var holes = await coverageHoles.GetHolesAsync(userId, windowStart, monthEnd, ct);
        var history = EstimateHistory.Resolve(monthStart, currentMonth, series.FirstDataDay, options.Value, holes);

        // The detected set describes the commitments held now, so a past month is read from its own history instead.
        var isPast = phase == MonthPhase.Past;
        var suggestions = BudgetEstimator.Suggest(
            history,
            series.Months,
            isPast ? [] : series.Commitments,
            isPast ? [] : series.EndedCommitments,
            monthStart);

        var monthKey = monthStart.ToString("yyyy-MM");
        var spentByLine = series.Months
            .Where(r => r.Month == monthKey && EstimateLines.IsEstimable(r.Line))
            .GroupBy(r => r.Line, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.SpendUsd), StringComparer.Ordinal);

        var overrideByLine = new Dictionary<string, Budget>(StringComparer.Ordinal);
        foreach (var budget in overrides)
            overrideByLine[normalization.Normalize(budget.Category)] = budget;

        var lines = suggestions.Keys.Union(spentByLine.Keys, StringComparer.Ordinal)
            .Union(overrideByLine.Keys, StringComparer.Ordinal)
            .ToList();

        var items = new List<BudgetSummaryItemDto>();
        foreach (var line in lines)
        {
            var suggestion = suggestions.GetValueOrDefault(line);
            var spent = spentByLine.GetValueOrDefault(line);
            overrideByLine.TryGetValue(line, out var edit);

            decimal? effective = edit is not null
                ? CurrencyConverter.ToUsd(edit.MonthlyLimit, edit.Currency)
                : suggestion?.Amount;

            // A line the history has nothing to say about (a median of 0), with no spend and no hand edit, is noise.
            if (edit is null && spent == 0m && (effective is null || effective == 0m))
                continue;

            items.Add(ToItem(line, edit, suggestion, effective, spent, phase, paceAsOf));
        }

        items = [.. items.OrderByDescending(i => i.Estimate ?? i.Spent).ThenBy(i => i.CategoryLabel, StringComparer.Ordinal)];

        return new BudgetSummaryResponse(
            year,
            month,
            items,
            items.Sum(i => i.Estimate ?? 0m),
            items.Sum(i => i.Spent),
            items.Sum(i => i.ExpectedMonthEnd),
            history.IsEnough,
            history.UsedMonths.Count,
            Projection.MinCompleteMonths,
            [.. history.Skipped.Select(s => new SkippedEstimateMonthDto(s.Month, s.Reason))],
            holes.Any(h => h.Overlaps(monthStart, monthEnd)));
    }

    private BudgetSummaryItemDto ToItem(
        string line, Budget? edit, LineSuggestion? suggestion, decimal? effective, decimal spent,
        MonthPhase phase, DateTime paceAsOf)
    {
        var amount = effective ?? 0m;
        var pace = BudgetPace.Calculate(spent, amount, paceAsOf);
        var isOver = effective is not null && spent > amount;

        // Already over is the louder signal; pace is for "heading there". A suggestion on a standing obligation is
        // never paced (see EstimateLines.IsCommitted), a hand-set amount always is.
        var isOffPace = phase == MonthPhase.Current
            && effective is not null
            && !isOver
            && (edit is not null || !EstimateLines.IsCommitted(line))
            && paceAsOf.Day >= BudgetPace.WindowStartDay
            && paceAsOf.Day <= BudgetPace.WindowEndDay
            && pace.PaceRatio >= BudgetPace.Tolerance;

        return new BudgetSummaryItemDto(
            edit?.Id,
            line,
            normalization.GetLabel(line),
            effective,
            suggestion?.Amount,
            edit is not null ? OverrideSource : suggestion?.Basis ?? NoSource,
            spent,
            effective is null ? 0m : amount - spent,
            isOver,
            "USD",
            pace.PaceRatio,
            pace.ProjectedMonthEndSpend,
            isOffPace,
            BudgetEstimator.ExpectedMonthEnd(line, effective, spent, phase, paceAsOf));
    }

    public const string OverrideSource = "override";
    public const string NoSource = "none";
}
