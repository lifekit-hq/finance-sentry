namespace FinanceSentry.Modules.Research.Domain.Screens;

using FinanceSentry.Modules.Research.Domain.Scoring;

/// <summary>The bars a company's latest fiscal year must clear (bound from <c>Screens:Fundamentals</c>).</summary>
public sealed record FundamentalsScreenThresholds(
    decimal MinRevenueGrowth,
    decimal MinOperatingCashFlowMargin,
    decimal MaxCapexIntensity,
    decimal MinStockholdersEquity,
    int MaxFactAgeDays);

/// <summary>
/// A company's latest fiscal year read by the fundamentals screen. A measure that cannot be computed is null and named
/// in <see cref="NotEvaluable"/>; it is never defaulted, so a filer with no capex line cannot read as capital-light.
/// </summary>
public sealed record FundamentalsScreenReading(
    bool Passed,
    DateOnly? PeriodEnd,
    decimal? RevenueGrowth,
    decimal? OperatingCashFlowMargin,
    decimal? CapexIntensity,
    decimal? StockholdersEquity,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> NotEvaluable);

/// <summary>
/// The fundamentals screen (discovery funnel stage 5): revenue growth, operating cash flow margin, capex intensity and
/// equity of the latest fiscal year, from the EDGAR facts the fundamentals chain already serves. Pure and deterministic,
/// built on <see cref="FundamentalMath"/> so the period selection (amended filings, year-earlier pairing) is the one
/// every other fundamentals reader uses. A name passes only when all four measures are evaluable and clear their bar.
/// </summary>
public static class FundamentalsScreenRules
{
    public const string RevenueGrowthMeasure = "revenue_growth";
    public const string OperatingCashFlowMarginMeasure = "operating_cash_flow_margin";
    public const string CapexIntensityMeasure = "capex_intensity";
    public const string EquityMeasure = "stockholders_equity";
    public const string NoAnnualRevenue = "no_annual_revenue";
    public const string StaleFacts = "stale_facts";

    private const int SamePeriodToleranceDays = 7;

    public static FundamentalsScreenReading Evaluate(
        IReadOnlyList<FundamentalFact> facts, FundamentalsScreenThresholds thresholds, DateOnly today)
    {
        var revenue = FundamentalMath.SelectPeriods(facts, FundamentalMath.RevenueConcept, ThesisPeriodType.Annual);
        if (revenue.Count == 0 || revenue[0].Value <= 0m)
        {
            return Unevaluable(null, NoAnnualRevenue);
        }

        var latest = revenue[0];
        if (today.DayNumber - latest.PeriodEnd.DayNumber > thresholds.MaxFactAgeDays)
        {
            return Unevaluable(latest.PeriodEnd, StaleFacts);
        }

        var growth = FundamentalMath.LatestYoy(facts, FundamentalMath.RevenueConcept, ThesisPeriodType.Annual);
        var ocf = SameYear(facts, CashFlowMath.OperatingCashFlowConcept, latest);
        var capex = SameYear(facts, CashFlowMath.CapexConcept, latest);
        var equity = SameYear(facts, "StockholdersEquity", latest);

        decimal? ocfMargin = ocf is null ? null : ocf.Value / latest.Value;
        decimal? capexIntensity = capex is null ? null : Math.Abs(capex.Value) / latest.Value;
        decimal? equityValue = equity?.Value;

        var notEvaluable = new List<string>();
        var failed = new List<string>();
        Check(RevenueGrowthMeasure, growth, g => g >= thresholds.MinRevenueGrowth, notEvaluable, failed);
        Check(OperatingCashFlowMarginMeasure, ocfMargin, m => m >= thresholds.MinOperatingCashFlowMargin, notEvaluable, failed);
        Check(CapexIntensityMeasure, capexIntensity, c => c <= thresholds.MaxCapexIntensity, notEvaluable, failed);
        Check(EquityMeasure, equityValue, e => e > thresholds.MinStockholdersEquity, notEvaluable, failed);

        return new FundamentalsScreenReading(
            notEvaluable.Count == 0 && failed.Count == 0,
            latest.PeriodEnd, growth, ocfMargin, capexIntensity, equityValue, failed, notEvaluable);
    }

    private static void Check(string measure, decimal? value, Func<decimal, bool> clears, List<string> notEvaluable, List<string> failed)
    {
        if (value is not { } v)
        {
            notEvaluable.Add(measure);
        }
        else if (!clears(v))
        {
            failed.Add(measure);
        }
    }

    // The fiscal-year fact of the concept for the same year as the revenue fact: same unit, period end within a week.
    private static FundamentalFact? SameYear(IReadOnlyList<FundamentalFact> facts, string concept, FundamentalFact revenue)
        => FundamentalMath.SelectPeriods(facts, concept, ThesisPeriodType.Annual)
            .Where(f => string.Equals(f.Unit, revenue.Unit, StringComparison.Ordinal)
                && Math.Abs(f.PeriodEnd.DayNumber - revenue.PeriodEnd.DayNumber) <= SamePeriodToleranceDays)
            .OrderBy(f => Math.Abs(f.PeriodEnd.DayNumber - revenue.PeriodEnd.DayNumber))
            .FirstOrDefault();

    private static FundamentalsScreenReading Unevaluable(DateOnly? periodEnd, string reason)
        => new(false, periodEnd, null, null, null, null, [], [reason]);
}
