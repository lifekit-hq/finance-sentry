namespace FinanceSentry.Modules.Research.Domain.Scoring;

using FinanceSentry.Modules.Research.Domain.ThesisMonitor;

/// <summary>
/// Pure, deterministic 0-100 fundamentals sub-score from EDGAR <see cref="FundamentalFact"/>s
/// (revenue YoY, gross margin level + trend, diluted EPS YoY). Reuses <see cref="FundamentalMath"/>'s
/// concept table and ratio helpers — no re-derivation of 017's concept mapping. Missing data or
/// divide-by-zero surface as null components (never faked), and the whole score is null when
/// nothing at all is evaluable (FR-002/FR-006).
/// </summary>
public static class FundamentalsScorer
{
    /// <summary>EDGAR datapoints per concept a caller must fetch for the YoY and margin-trend windows to resolve.</summary>
    public const int FactsPerConcept = 8;

    private const decimal MinScore = 0m;
    private const decimal MaxScore = 100m;
    private const decimal Midpoint = 50m;

    // Every 25 percentage points of YoY growth moves a growth component by 25 points.
    private const decimal YoyScaleFactor = 100m;

    // A 50% gross margin maps to a full 100 on the margin-level component.
    private const decimal MarginLevelScaleFactor = 200m;

    // Every 5 percentage points of QoQ margin expansion moves the trend component by 15 points.
    private const decimal MarginTrendScaleFactor = 300m;

    // Spec 019 FR-004: margin trend spans the last 4 quarters (latest vs one year earlier) —
    // quarter-over-quarter noise is not a trend.
    private const int MarginTrendLookbackQuarters = 4;

    public static (
        int? Score,
        decimal? RevenueYoy,
        decimal? GrossMarginLatest,
        decimal? GrossMarginTrend,
        decimal? EpsYoy,
        IReadOnlyList<string> NotEvaluableReasons) Score(IReadOnlyList<FundamentalFact> facts)
    {
        if (facts.Count == 0)
        {
            return (null, null, null, null, null, ["no_fundamentals_data"]);
        }

        var reasons = new List<string>();
        var components = new List<decimal>();

        var revenueYoy = FundamentalMath.LatestYoy(facts, FundamentalMath.YoyConceptByMetric[ThesisMetric.RevenueYoy]);
        if (revenueYoy is { } rev)
        {
            components.Add(Clamp(Midpoint + (rev * YoyScaleFactor)));
        }
        else
        {
            reasons.Add("revenue_yoy_not_evaluable");
        }

        var (grossNumerator, grossDenominator) = FundamentalMath.MarginConceptsByMetric[ThesisMetric.GrossMargin];
        var marginLatest = FundamentalMath.LatestMargin(facts, grossNumerator, grossDenominator);
        if (marginLatest is { } latestMargin)
        {
            components.Add(Clamp(latestMargin * MarginLevelScaleFactor));
        }
        else
        {
            reasons.Add("gross_margin_not_evaluable");
        }

        var marginTrend = FundamentalMath.MarginTrend(facts, grossNumerator, grossDenominator, MarginTrendLookbackQuarters);
        if (marginTrend is { } trend)
        {
            components.Add(Clamp(Midpoint + (trend * MarginTrendScaleFactor)));
        }
        else
        {
            reasons.Add("gross_margin_trend_not_evaluable");
        }

        var epsYoy = FundamentalMath.LatestYoy(facts, FundamentalMath.YoyConceptByMetric[ThesisMetric.EpsYoy]);
        if (epsYoy is { } eps)
        {
            components.Add(Clamp(Midpoint + (eps * YoyScaleFactor)));
        }
        else
        {
            reasons.Add("eps_yoy_not_evaluable");
        }

        if (components.Count == 0)
        {
            return (null, revenueYoy, marginLatest, marginTrend, epsYoy, reasons);
        }

        var score = (int)Math.Round(components.Average(), MidpointRounding.AwayFromZero);
        return (score, revenueYoy, marginLatest, marginTrend, epsYoy, reasons);
    }

    private static decimal Clamp(decimal value) => Math.Clamp(value, MinScore, MaxScore);
}
