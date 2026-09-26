namespace FinanceSentry.Modules.Research.Domain.ThesisMonitor;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Scoring;

/// <summary>
/// Pure, deterministic evaluation of a single <see cref="ThesisInvalidationTrigger"/> against a
/// target ticker's fundamentals and/or price history. No EF, no HTTP, no LLM — the spine of the
/// thesis-break monitor (FR-006/FR-007/SC-001/SC-002). Concept mapping + period selection live in
/// <see cref="FundamentalMath"/>, shared with 019's fundamentals scorer.
/// </summary>
public static class ThesisBreakEvaluator
{
    private const string FiscalYearPeriod = FundamentalMath.FiscalYearPeriod;

    private static readonly IReadOnlyDictionary<string, string> RawConceptByMetric = FundamentalMath.RawConceptByMetric;

    private static readonly IReadOnlyDictionary<string, (string Numerator, string Denominator)> MarginConceptsByMetric =
        FundamentalMath.MarginConceptsByMetric;

    private static readonly IReadOnlyDictionary<string, string> YoyConceptByMetric = FundamentalMath.YoyConceptByMetric;

    public static TriggerVerdict Evaluate(
        ThesisInvalidationTrigger trigger,
        DateTimeOffset thesisCreatedAt,
        IReadOnlyList<FundamentalFact> fundamentals,
        IReadOnlyList<DailyClose> dailyCloses,
        decimal? entryPrice = null)
    {
        if (!ThesisTriggerEvaluability.IsStructurallyEvaluable(trigger, out var reason))
        {
            return new TriggerVerdict.NonEvaluable(reason!);
        }

        return ThesisMetric.IsPriceMetric(trigger.Metric)
            ? EvaluatePrice(trigger, thesisCreatedAt, dailyCloses, entryPrice)
            : EvaluateFundamentals(trigger, fundamentals);
    }

    private static TriggerVerdict EvaluateFundamentals(
        ThesisInvalidationTrigger trigger, IReadOnlyList<FundamentalFact> fundamentals)
    {
        if (RawConceptByMetric.TryGetValue(trigger.Metric, out var rawConcept))
        {
            return EvaluateRaw(trigger, fundamentals, rawConcept);
        }

        if (MarginConceptsByMetric.TryGetValue(trigger.Metric, out var marginConcepts))
        {
            return EvaluateMargin(trigger, fundamentals, marginConcepts.Numerator, marginConcepts.Denominator);
        }

        if (YoyConceptByMetric.TryGetValue(trigger.Metric, out var yoyConcept))
        {
            return EvaluateYoy(trigger, fundamentals, yoyConcept);
        }

        return new TriggerVerdict.NonEvaluable(NonEvaluableReason.UnsupportedMetric);
    }

    private static TriggerVerdict EvaluateRaw(
        ThesisInvalidationTrigger trigger, IReadOnlyList<FundamentalFact> fundamentals, string concept)
    {
        var periods = SelectPeriods(fundamentals, concept, trigger.PeriodType);
        if (periods.Count == 0)
        {
            return new TriggerVerdict.NonEvaluable(NonEvaluableReason.NoFundamentals);
        }

        if (periods.Count < trigger.ConsecutivePeriods)
        {
            return new TriggerVerdict.NonEvaluable(NonEvaluableReason.InsufficientPeriods);
        }

        var window = periods.Take(trigger.ConsecutivePeriods).ToList();
        var values = window.Select(f => f.Value).ToArray();
        var labels = window.Select(Label).ToArray();

        return BuildVerdict(trigger, values, labels);
    }

    private static TriggerVerdict EvaluateMargin(
        ThesisInvalidationTrigger trigger,
        IReadOnlyList<FundamentalFact> fundamentals,
        string numeratorConcept,
        string denominatorConcept)
    {
        var numeratorPeriods = SelectPeriods(fundamentals, numeratorConcept, trigger.PeriodType);
        var denominatorPeriods = SelectPeriods(fundamentals, denominatorConcept, trigger.PeriodType)
            .ToDictionary(f => f.PeriodEnd);

        if (numeratorPeriods.Count == 0 || denominatorPeriods.Count == 0)
        {
            return new TriggerVerdict.NonEvaluable(NonEvaluableReason.NoFundamentals);
        }

        var values = new List<decimal>();
        var labels = new List<string>();

        foreach (var fact in numeratorPeriods)
        {
            if (values.Count == trigger.ConsecutivePeriods)
            {
                break;
            }

            if (!denominatorPeriods.TryGetValue(fact.PeriodEnd, out var denominatorFact))
            {
                continue;
            }

            if (denominatorFact.Value == 0)
            {
                return new TriggerVerdict.NonEvaluable(NonEvaluableReason.DivideByZero);
            }

            values.Add(fact.Value / denominatorFact.Value);
            labels.Add(Label(fact));
        }

        if (values.Count < trigger.ConsecutivePeriods)
        {
            return new TriggerVerdict.NonEvaluable(NonEvaluableReason.InsufficientPeriods);
        }

        return BuildVerdict(trigger, [.. values], [.. labels]);
    }

    private static TriggerVerdict EvaluateYoy(
        ThesisInvalidationTrigger trigger, IReadOnlyList<FundamentalFact> fundamentals, string concept)
    {
        var periods = SelectPeriods(fundamentals, concept, trigger.PeriodType);
        if (periods.Count == 0)
        {
            return new TriggerVerdict.NonEvaluable(NonEvaluableReason.NoFundamentals);
        }

        var values = new List<decimal>();
        var labels = new List<string>();

        foreach (var fact in periods)
        {
            if (values.Count == trigger.ConsecutivePeriods)
            {
                break;
            }

            // Pair by PeriodEnd, not by EDGAR fy/fp labels — those describe the filing, not the
            // fact's own period, and can silently misalign the YoY pair (2026-07-08 review).
            var prior = Scoring.FundamentalMath.PriorYearFact(fact, periods);
            if (prior is null)
            {
                continue;
            }

            if (prior.Value == 0)
            {
                return new TriggerVerdict.NonEvaluable(NonEvaluableReason.DivideByZero);
            }

            values.Add((fact.Value - prior.Value) / prior.Value);
            labels.Add(Label(fact));
        }

        if (values.Count < trigger.ConsecutivePeriods)
        {
            return new TriggerVerdict.NonEvaluable(NonEvaluableReason.InsufficientPeriods);
        }

        return BuildVerdict(trigger, [.. values], [.. labels]);
    }

    private static TriggerVerdict EvaluatePrice(
        ThesisInvalidationTrigger trigger,
        DateTimeOffset thesisCreatedAt,
        IReadOnlyList<DailyClose> dailyCloses,
        decimal? entryPrice = null)
    {
        var since = DateOnly.FromDateTime(thesisCreatedAt.UtcDateTime);
        var closes = dailyCloses.Where(c => c.Date >= since).OrderBy(c => c.Date).ToList();

        if (closes.Count == 0)
        {
            return new TriggerVerdict.NonEvaluable(NonEvaluableReason.NoPriceHistory);
        }

        if (closes.Count < trigger.ConsecutivePeriods)
        {
            return new TriggerVerdict.NonEvaluable(NonEvaluableReason.InsufficientPeriods);
        }

        var trailing = closes.TakeLast(trigger.ConsecutivePeriods).ToList();
        var values = new decimal[trailing.Count];
        var labels = new string[trailing.Count];

        for (var i = 0; i < trailing.Count; i++)
        {
            var day = trailing[i];

            // price_drawdown: anchor to entryPrice when recorded (FR-draw-1).
            // If entryPrice is absent, fall back to the first close on/after thesis creation — a
            // closer proxy to entry than the rolling max, which was anchored to the all-time peak
            // and caused false breaks for beaten-down names (e.g. MHPC reported 0.84 vs ~0.10).
            // price_return always anchors to the first close (unchanged behaviour).
            decimal baseline;
            if (trigger.Metric == ThesisMetric.PriceDrawdown)
            {
                baseline = entryPrice ?? closes[0].Close;
            }
            else
            {
                var upToDay = closes.Where(c => c.Date <= day.Date).ToList();
                baseline = upToDay[0].Close;
            }

            if (baseline == 0)
            {
                return new TriggerVerdict.NonEvaluable(NonEvaluableReason.DivideByZero);
            }

            values[i] = trigger.Metric == ThesisMetric.PriceDrawdown
                ? (baseline - day.Close) / baseline
                : (day.Close - baseline) / baseline;
            labels[i] = day.Date.ToString("yyyy-MM-dd");
        }

        return BuildVerdict(trigger, values, labels);
    }

    private static TriggerVerdict BuildVerdict(
        ThesisInvalidationTrigger trigger, decimal[] values, string[] labels)
    {
        var allBreach = values.All(v => Breaches(v, trigger.Direction, trigger.Threshold));

        return allBreach
            ? new TriggerVerdict.Breached(trigger.Metric, values, labels, trigger.Threshold, trigger.Direction)
            : new TriggerVerdict.Held();
    }

    private static bool Breaches(decimal value, string direction, decimal threshold) => direction switch
    {
        ThesisTriggerDirection.LessThan => value < threshold,
        ThesisTriggerDirection.GreaterThan => value > threshold,
        _ => false,
    };

    private static IReadOnlyList<FundamentalFact> SelectPeriods(
        IReadOnlyList<FundamentalFact> facts, string concept, ThesisPeriodType periodType)
        => FundamentalMath.SelectPeriods(facts, concept, periodType);

    private static string Label(FundamentalFact fact) => FundamentalMath.Label(fact);
}
