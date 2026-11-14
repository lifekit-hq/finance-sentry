namespace FinanceSentry.Modules.Research.Domain.Scoring;

using FinanceSentry.Modules.Research.Domain.Fundamentals;

/// <summary>
/// Cash-flow handling for <c>get_fundamentals</c> (#1376). XBRL cash-flow facts are durations measured
/// from the fiscal-year start (a Q2 10-Q reports six months, a Q3 nine, the 10-K twelve), so a quarter is
/// not a reported fact: it is the year-to-date figure less the previous year-to-date figure of the same
/// year (Q4 = the full year less nine months). Pure and deterministic; a quarter whose subtrahend is
/// missing is left out, never zero-filled.
/// </summary>
public static class CashFlowMath
{
    public const string OperatingCashFlowConcept = "OperatingCashFlow";
    public const string CapexConcept = "Capex";
    public const string FreeCashFlowConcept = "FreeCashFlow";

    public static readonly IReadOnlyList<string> Concepts = [OperatingCashFlowConcept, CapexConcept];

    private const int DaysPerQuarter = 91;
    private const int QuarterToleranceDays = 20;
    private const int MaxQuarters = 4;
    private const int PriorYtdToleranceDays = 20;

    public static bool IsCashFlowConcept(string concept) => Concepts.Contains(concept);

    /// <summary>
    /// One fact per discrete period, newest first: quarters labelled Q1-Q4 (derived from year-to-date
    /// facts where the filer reports only those) and full years labelled FY. Facts without a
    /// <see cref="FundamentalFact.PeriodStart"/> cannot be placed in a year and are ignored.
    /// </summary>
    public static IReadOnlyList<FundamentalFact> DiscretePeriods(IEnumerable<FundamentalFact> facts)
    {
        // One fact per (start, end): amended forms win, then the latest filing, then the last read. A later
        // filing repeats the period as a prior-year comparative under its own fiscal year, so the label comes
        // from the earliest filing that reported the period.
        var durations = facts
            .Where(f => f.PeriodStart is not null && f.PeriodStart < f.PeriodEnd)
            .GroupBy(f => (f.PeriodStart, f.PeriodEnd))
            .Select(g => g
                .OrderBy(f => f.Form.EndsWith("/A", StringComparison.OrdinalIgnoreCase))
                .ThenBy(f => f.FiscalYear ?? int.MinValue)
                .Last() with { FiscalYear = g.Min(f => f.FiscalYear) })
            .ToList();

        // A twelve-month figure in a 10-Q is a trailing-twelve-month one (Amazon files them beside the quarter),
        // not a fiscal year: only an annual filing's twelve months is the year.
        durations.RemoveAll(f => QuartersIn(f) == MaxQuarters && f.Form.StartsWith("10-Q", StringComparison.OrdinalIgnoreCase));

        // The first day of each fiscal year seen: every half-year / nine-month / full-year duration begins on one.
        var yearStarts = durations
            .Where(f => QuartersIn(f) is > 1)
            .Select(f => f.PeriodStart!.Value)
            .Distinct()
            .ToList();

        var result = new List<FundamentalFact>();
        foreach (var fact in durations)
        {
            var quarters = QuartersIn(fact);
            if (quarters is null)
            {
                continue;
            }

            if (quarters == 1)
            {
                // A three-month figure is Q1 only when it starts the fiscal year; filers that also report the
                // discrete later quarters (Amazon's 10-Qs) would otherwise label every quarter Q1.
                if (QuarterLabel(fact, yearStarts) is { } label)
                {
                    result.Add(fact with { FiscalPeriod = label });
                }

                continue;
            }

            if (quarters == MaxQuarters)
            {
                result.Add(fact with { FiscalPeriod = FundamentalMath.FiscalYearPeriod });
                if (DerivedQuarter(fact, durations, MaxQuarters) is { } q4)
                {
                    result.Add(q4);
                }

                continue;
            }

            var discrete = DerivedQuarter(fact, durations, quarters.Value);
            if (discrete is not null)
            {
                result.Add(discrete);
            }
        }

        return result
            .GroupBy(f => (f.PeriodStart, f.PeriodEnd, f.FiscalPeriod))
            .Select(g => g.First())
            .OrderByDescending(f => f.PeriodEnd)
            .ThenBy(f => f.FiscalPeriod, StringComparer.Ordinal)
            .ToList();
    }

    // Q1..Q4 of a three-month duration: its offset from the fiscal-year start it falls in, or - with no year
    // start known - the filing's own Q1..Q3 label. Null when the quarter cannot be placed.
    private static string? QuarterLabel(FundamentalFact fact, IReadOnlyList<DateOnly> yearStarts)
    {
        var start = fact.PeriodStart!.Value;
        var yearStart = yearStarts.Where(s => s <= start.AddDays(QuarterToleranceDays)).OrderByDescending(s => s).FirstOrDefault();
        if (yearStart != default)
        {
            var offset = (int)Math.Round((start.DayNumber - yearStart.DayNumber) / (double)DaysPerQuarter);
            if (offset >= 0)
            {
                // Modulo: a quarter of a later year with no year-to-date fact of its own still counts from a known start.
                return $"Q{(offset % MaxQuarters) + 1}";
            }
        }

        return fact.FiscalPeriod is ['Q', >= '1' and <= '3'] ? fact.FiscalPeriod : null;
    }

    // The discrete quarter ending at `fact.PeriodEnd` whose year-to-date length is `quarters` (2-4) quarters:
    // the fact less the same-year YTD one quarter shorter.
    private static FundamentalFact? DerivedQuarter(
        FundamentalFact fact, IReadOnlyList<FundamentalFact> durations, int quarters)
    {
        var priorEnd = fact.PeriodEnd.AddMonths(-3);
        var prior = durations
            .Where(d => d.PeriodStart == fact.PeriodStart
                        && string.Equals(d.Unit, fact.Unit, StringComparison.Ordinal)
                        && Math.Abs(d.PeriodEnd.DayNumber - priorEnd.DayNumber) <= PriorYtdToleranceDays
                        && QuartersIn(d) == quarters - 1)
            .OrderBy(d => Math.Abs(d.PeriodEnd.DayNumber - priorEnd.DayNumber))
            .FirstOrDefault();
        if (prior is null)
        {
            return null;
        }

        return fact with
        {
            Value = fact.Value - prior.Value,
            PeriodStart = prior.PeriodEnd.AddDays(1),
            FiscalPeriod = $"Q{quarters}",
            Label = $"{fact.Label} (discrete quarter: year-to-date less prior year-to-date)",
        };
    }

    // How many quarters a duration spans (1-4), or null when it is none of a quarter, half, nine months, year.
    private static int? QuartersIn(FundamentalFact fact)
    {
        var days = fact.PeriodEnd.DayNumber - fact.PeriodStart!.Value.DayNumber;
        for (var quarters = 1; quarters <= MaxQuarters; quarters++)
        {
            if (Math.Abs(days - (quarters * DaysPerQuarter)) <= QuarterToleranceDays)
            {
                return quarters;
            }
        }

        return null;
    }
}

/// <summary>Free cash flow and return on equity derived from the chain's facts (#1376).</summary>
public static class DerivedFundamentalsMath
{
    private const int YearEarlierToleranceDays = 45;

    public static IReadOnlyList<FreeCashFlowPeriod> FreeCashFlow(
        IReadOnlyList<FundamentalFact> facts, int quarters, int years)
    {
        var quarterly = BuildBasis(facts, FundamentalsBasis.Quarterly, isAnnual: false).Take(quarters);
        var annual = BuildBasis(facts, FundamentalsBasis.Annual, isAnnual: true).Take(years);
        return quarterly.Concat(annual).ToList();
    }

    public static IReadOnlyList<ReturnOnEquityPeriod> ReturnOnEquity(IReadOnlyList<FundamentalFact> facts, int years)
    {
        var income = Annual(facts, "NetIncome");
        var equity = Annual(facts, "StockholdersEquity");
        return income.Keys.Union(equity.Keys)
            .OrderByDescending(end => end)
            .Take(years)
            .Select(end =>
            {
                income.TryGetValue(end, out var ni);
                equity.TryGetValue(end, out var eq);
                int? fy = end.Year;
                if (ni is null || eq is null)
                {
                    return new ReturnOnEquityPeriod(
                        end, fy, ni?.Value, eq?.Value, null,
                        ni is null ? "net income not reported for the year" : "stockholders' equity not reported for the year");
                }

                if (eq.Value <= 0)
                {
                    return new ReturnOnEquityPeriod(end, fy, ni.Value, eq.Value, null, "stockholders' equity is not positive");
                }

                return new ReturnOnEquityPeriod(end, fy, ni.Value, eq.Value, ni.Value / eq.Value, null);
            })
            .ToList();
    }

    private static Dictionary<DateOnly, FundamentalFact> Annual(IReadOnlyList<FundamentalFact> facts, string concept)
        => FundamentalMath.SelectPeriods(facts, concept, ThesisPeriodType.Annual)
            .ToDictionary(f => f.PeriodEnd);

    private static List<FreeCashFlowPeriod> BuildBasis(IReadOnlyList<FundamentalFact> facts, string basis, bool isAnnual)
    {
        bool InBasis(FundamentalFact f)
            => string.Equals(f.FiscalPeriod, FundamentalMath.FiscalYearPeriod, StringComparison.OrdinalIgnoreCase) == isAnnual;

        Dictionary<(DateOnly, string?), FundamentalFact> Index(string concept)
            => facts
                .Where(f => f.Concept == concept && InBasis(f))
                .GroupBy(f => (f.PeriodEnd, f.FiscalPeriod))
                .ToDictionary(g => g.Key, g => g.First());

        var ocf = Index(CashFlowMath.OperatingCashFlowConcept);
        var capex = Index(CashFlowMath.CapexConcept);

        var raw = ocf.Keys.Union(capex.Keys)
            .OrderByDescending(k => k.Item1)
            .Select(key =>
            {
                ocf.TryGetValue(key, out var o);
                capex.TryGetValue(key, out var c);
                var source = o ?? c!;
                if (o is null || c is null)
                {
                    var reason = o is null
                        ? "operating cash flow not reported for the period"
                        : "capex not reported for the period (not assumed zero)";
                    return new FreeCashFlowPeriod(
                        basis, key.Item1, key.Item2, source.FiscalYear, source.Unit, o?.Value, c?.Value, null, null, reason);
                }

                if (!string.Equals(o.Unit, c.Unit, StringComparison.Ordinal))
                {
                    return new FreeCashFlowPeriod(
                        basis, key.Item1, key.Item2, o.FiscalYear, o.Unit, o.Value, c.Value, null, null,
                        "operating cash flow and capex are reported in different units");
                }

                return new FreeCashFlowPeriod(
                    basis, key.Item1, key.Item2, o.FiscalYear, o.Unit, o.Value, c.Value, o.Value - c.Value, null, null);
            })
            .ToList();

        return raw
            .Select(p =>
            {
                var (change, reason) = Yoy(p, raw);
                return p with { YoyChange = change, YoyReason = reason };
            })
            .ToList();
    }

    private static (decimal? Change, string? Reason) Yoy(FreeCashFlowPeriod current, IReadOnlyList<FreeCashFlowPeriod> all)
    {
        if (current.FreeCashFlow is not { } now)
        {
            return (null, null);
        }

        var target = current.PeriodEnd.AddYears(-1);
        var prior = all
            .Where(p => p.FreeCashFlow is not null
                        && p.PeriodEnd < current.PeriodEnd
                        && Math.Abs(p.PeriodEnd.DayNumber - target.DayNumber) <= YearEarlierToleranceDays)
            .OrderBy(p => Math.Abs(p.PeriodEnd.DayNumber - target.DayNumber))
            .FirstOrDefault();
        if (prior?.FreeCashFlow is not { } before)
        {
            return (null, "no free cash flow for the same period a year earlier in the history read (it may fall outside the facts the source provides)");
        }

        return before == 0
            ? (null, "free cash flow a year earlier was zero")
            : ((now - before) / Math.Abs(before), null);
    }
}
