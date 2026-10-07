namespace FinanceSentry.Modules.Research.Domain.Scoring;

using FinanceSentry.Modules.Research.Domain.ThesisMonitor;

/// <summary>
/// Shared EDGAR-concept mapping + pure ratio math for anything that evaluates fundamentals
/// (017's <see cref="ThesisBreakEvaluator"/> and 019's <c>FundamentalsScorer</c>). One concept
/// table, one place division-by-zero is guarded — no silent duplication of either (per 019 plan).
/// </summary>
public static class FundamentalMath
{
    public const string FiscalYearPeriod = "FY";

    public static readonly IReadOnlyDictionary<string, string> RawConceptByMetric = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ThesisMetric.Revenue] = "Revenue",
        [ThesisMetric.NetIncome] = "NetIncome",
        [ThesisMetric.DilutedEps] = "DilutedEPS",
    };

    public static readonly IReadOnlyDictionary<string, (string Numerator, string Denominator)> MarginConceptsByMetric =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            [ThesisMetric.GrossMargin] = ("GrossProfit", "Revenue"),
            [ThesisMetric.OperatingMargin] = ("OperatingIncome", "Revenue"),
            [ThesisMetric.NetMargin] = ("NetIncome", "Revenue"),
        };

    public static readonly IReadOnlyDictionary<string, string> YoyConceptByMetric = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ThesisMetric.RevenueYoy] = "Revenue",
        [ThesisMetric.NetIncomeYoy] = "NetIncome",
        [ThesisMetric.OperatingIncomeYoy] = "OperatingIncome",
        [ThesisMetric.EpsYoy] = "DilutedEPS",
    };

    /// <summary>
    /// Newest-first, one fact per period-end, for the given concept + period cadence. Duplicate
    /// facts for one period (original + amended filing, or a comparative re-reported in a later
    /// filing) resolve deterministically: amended forms (…/A) win, then the latest filing
    /// (highest FiscalYear), then Form ordinal — never source order.
    /// </summary>
    public static IReadOnlyList<FundamentalFact> SelectPeriods(
        IReadOnlyList<FundamentalFact> facts, string concept, ThesisPeriodType periodType)
        => facts
            .Where(f => string.Equals(f.Concept, concept, StringComparison.Ordinal) && MatchesPeriodType(f, periodType))
            .GroupBy(f => f.PeriodEnd)
            .Select(g => g
                .OrderByDescending(f => f.Form.EndsWith("/A", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f.FiscalYear ?? int.MinValue)
                .ThenBy(f => f.Form, StringComparer.Ordinal)
                .First())
            .OrderByDescending(f => f.PeriodEnd)
            .ToList();

    /// <summary>
    /// The fact one fiscal year before <paramref name="current"/>, paired by PERIOD END (closest
    /// period ending within ±<paramref name="toleranceDays"/> of one year earlier) — never by
    /// EDGAR's fy/fp labels, which describe the filing rather than the fact's own period and can
    /// silently misalign the pair. Deterministic: smallest distance, then earlier date.
    /// </summary>
    public static FundamentalFact? PriorYearFact(
        FundamentalFact current, IReadOnlyList<FundamentalFact> periods, int toleranceDays = 45)
    {
        var target = current.PeriodEnd.AddYears(-1);
        return periods
            .Where(p => p.PeriodEnd < current.PeriodEnd
                        && Math.Abs(p.PeriodEnd.DayNumber - target.DayNumber) <= toleranceDays)
            .OrderBy(p => Math.Abs(p.PeriodEnd.DayNumber - target.DayNumber))
            .ThenBy(p => p.PeriodEnd)
            .FirstOrDefault();
    }

    public static bool MatchesPeriodType(FundamentalFact fact, ThesisPeriodType periodType) => periodType switch
    {
        ThesisPeriodType.Annual => string.Equals(fact.FiscalPeriod, FiscalYearPeriod, StringComparison.OrdinalIgnoreCase),
        _ => fact.FiscalPeriod is not null && !string.Equals(fact.FiscalPeriod, FiscalYearPeriod, StringComparison.OrdinalIgnoreCase),
    };

    public static string Label(FundamentalFact fact)
        => fact.FiscalYear is { } year
            ? $"{year}{fact.FiscalPeriod ?? FiscalYearPeriod}"
            : fact.PeriodEnd.ToString("yyyy-MM-dd");

    /// <summary>
    /// A single period's ratio (e.g. gross margin) — null denominator/zero denominator both
    /// surface as "not evaluable" to the caller rather than throwing or defaulting to 0.
    /// </summary>
    public static decimal? SafeRatio(decimal numerator, decimal denominator)
        => denominator == 0 ? null : numerator / denominator;

    /// <summary>Year-over-year change for a same-fiscal-period pair; null when the prior value is zero.</summary>
    public static decimal? SafeYoy(decimal current, decimal prior)
        => prior == 0 ? null : (current - prior) / prior;

    /// <summary>
    /// Latest year-over-year change of a raw concept at the given cadence (default quarterly; matches
    /// the same fiscal period one year earlier), or null when not evaluable. Used by 019's fundamentals scorer.
    /// </summary>
    public static decimal? LatestYoy(
        IReadOnlyList<FundamentalFact> facts, string concept, ThesisPeriodType periodType = ThesisPeriodType.Quarter)
    {
        var periods = SelectPeriods(facts, concept, periodType);
        if (periods.Count == 0)
        {
            return null;
        }

        var latest = periods[0];
        var prior = PriorYearFact(latest, periods);
        if (prior is null)
        {
            return null;
        }

        // Growth off a loss-making base is meaningless ((cur - prior) / prior flips sign: a loss-to-profit
        // turnaround reads as a collapse). Annual basis treats it as not evaluable; the quarterly path
        // keeps its historical behaviour so domestic filers score exactly as before.
        return periodType == ThesisPeriodType.Annual && prior.Value < 0 ? null : SafeYoy(latest.Value, prior.Value);
    }

    /// <summary>Latest margin (numerator/denominator) of the given cadence, or null when not evaluable.</summary>
    public static decimal? LatestMargin(
        IReadOnlyList<FundamentalFact> facts, string numeratorConcept, string denominatorConcept,
        ThesisPeriodType periodType = ThesisPeriodType.Quarter)
    {
        var margins = MarginSeries(facts, numeratorConcept, denominatorConcept, periodType);
        return margins.Count == 0 ? null : margins[0].Margin;
    }

    /// <summary>
    /// Change in margin between the latest period and the period <paramref name="lookback"/> periods
    /// earlier (default 4 quarters = one year; pass 1 with annual periods). Null when fewer than
    /// <paramref name="lookback"/>+1 margins exist.
    /// </summary>
    public static decimal? MarginTrend(
        IReadOnlyList<FundamentalFact> facts, string numeratorConcept, string denominatorConcept, int lookback = 4,
        ThesisPeriodType periodType = ThesisPeriodType.Quarter)
    {
        var margins = MarginSeries(facts, numeratorConcept, denominatorConcept, periodType);
        return margins.Count <= lookback ? null : margins[0].Margin - margins[lookback].Margin;
    }

    private static IReadOnlyList<(DateOnly PeriodEnd, decimal Margin)> MarginSeries(
        IReadOnlyList<FundamentalFact> facts, string numeratorConcept, string denominatorConcept,
        ThesisPeriodType periodType)
    {
        var numerators = SelectPeriods(facts, numeratorConcept, periodType);
        var denominators = SelectPeriods(facts, denominatorConcept, periodType)
            .ToDictionary(f => f.PeriodEnd, f => f.Value);

        var series = new List<(DateOnly, decimal)>();
        foreach (var num in numerators)
        {
            if (denominators.TryGetValue(num.PeriodEnd, out var denom))
            {
                var margin = SafeRatio(num.Value, denom);
                if (margin is not null)
                {
                    series.Add((num.PeriodEnd, margin.Value));
                }
            }
        }

        return series;
    }
}
