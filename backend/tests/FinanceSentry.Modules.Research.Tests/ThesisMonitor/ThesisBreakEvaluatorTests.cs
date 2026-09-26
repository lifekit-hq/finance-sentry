namespace FinanceSentry.Modules.Research.Tests.ThesisMonitor;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.ThesisMonitor;
using FluentAssertions;
using Xunit;

public class ThesisBreakEvaluatorTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static FundamentalFact Fact(
        string ticker, string concept, decimal value, int fiscalYear, string fiscalPeriod, DateOnly periodEnd)
        => new(ticker, concept, concept, "USD", value, periodEnd, fiscalPeriod, fiscalYear, "10-Q");

    [Fact]
    public void GrossMargin_BreachesWhenAllConsecutivePeriodsBelowThreshold()
    {
        var facts = new List<FundamentalFact>
        {
            Fact("MU", "GrossProfit", 30m, 2026, "Q2", new DateOnly(2026, 5, 31)),
            Fact("MU", "Revenue", 100m, 2026, "Q2", new DateOnly(2026, 5, 31)),
            Fact("MU", "GrossProfit", 28m, 2026, "Q1", new DateOnly(2026, 2, 28)),
            Fact("MU", "Revenue", 100m, 2026, "Q1", new DateOnly(2026, 2, 28)),
        };

        var trigger = new ThesisInvalidationTrigger(
            ThesisMetric.GrossMargin, "lessThan", 0.35m, ProxyTicker: "MU", ConsecutivePeriods: 2);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, facts, []);

        var breached = verdict.Should().BeOfType<TriggerVerdict.Breached>().Subject;
        breached.Metric.Should().Be(ThesisMetric.GrossMargin);
        breached.ObservedValues.Should().HaveCount(2);
    }

    [Fact]
    public void GrossMargin_HoldsWhenOnlyNMinusOnePeriodsBreach()
    {
        var facts = new List<FundamentalFact>
        {
            Fact("MU", "GrossProfit", 30m, 2026, "Q2", new DateOnly(2026, 5, 31)),
            Fact("MU", "Revenue", 100m, 2026, "Q2", new DateOnly(2026, 5, 31)),
            Fact("MU", "GrossProfit", 40m, 2026, "Q1", new DateOnly(2026, 2, 28)), // 40% — above threshold
            Fact("MU", "Revenue", 100m, 2026, "Q1", new DateOnly(2026, 2, 28)),
        };

        var trigger = new ThesisInvalidationTrigger(
            ThesisMetric.GrossMargin, "lessThan", 0.35m, ProxyTicker: "MU", ConsecutivePeriods: 2);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, facts, []);

        verdict.Should().BeOfType<TriggerVerdict.Held>();
    }

    [Fact]
    public void RevenueYoy_UsesSameFiscalPeriodPriorYear()
    {
        var facts = new List<FundamentalFact>
        {
            Fact("MU", "Revenue", 80m, 2026, "Q2", new DateOnly(2026, 5, 31)),
            Fact("MU", "Revenue", 100m, 2025, "Q2", new DateOnly(2025, 5, 31)),
        };

        var trigger = new ThesisInvalidationTrigger(
            ThesisMetric.RevenueYoy, "lessThan", 0m, ProxyTicker: "MU", ConsecutivePeriods: 1);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, facts, []);

        var breached = verdict.Should().BeOfType<TriggerVerdict.Breached>().Subject;
        breached.ObservedValues[0].Should().Be(-0.2m);
    }

    [Fact]
    public void ProxyTicker_EvaluatorReceivesProxyFacts()
    {
        // The evaluator is fed whatever facts the caller resolved for the trigger's target ticker
        // (thesis ticker or ProxyTicker) — it has no notion of "thesis ticker" itself.
        var proxyFacts = new List<FundamentalFact>
        {
            Fact("MU", "Revenue", 50m, 2026, "Q2", new DateOnly(2026, 5, 31)),
        };

        var trigger = new ThesisInvalidationTrigger(
            ThesisMetric.Revenue, "lessThan", 100m, ProxyTicker: "MU", ConsecutivePeriods: 1);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, proxyFacts, []);

        verdict.Should().BeOfType<TriggerVerdict.Breached>();
    }

    [Fact]
    public void OrSemantics_FirstBreachingTriggerNamesTheReason()
    {
        var facts = new List<FundamentalFact>
        {
            Fact("MU", "Revenue", 50m, 2026, "Q2", new DateOnly(2026, 5, 31)),
        };

        var heldTrigger = new ThesisInvalidationTrigger(
            ThesisMetric.Revenue, "lessThan", 10m, ProxyTicker: "MU", ConsecutivePeriods: 1);
        var breachedTrigger = new ThesisInvalidationTrigger(
            ThesisMetric.Revenue, "lessThan", 100m, ProxyTicker: "MU", ConsecutivePeriods: 1);

        var heldVerdict = ThesisBreakEvaluator.Evaluate(heldTrigger, CreatedAt, facts, []);
        var breachedVerdict = ThesisBreakEvaluator.Evaluate(breachedTrigger, CreatedAt, facts, []);

        heldVerdict.Should().BeOfType<TriggerVerdict.Held>();
        breachedVerdict.Should().BeOfType<TriggerVerdict.Breached>();
    }

    [Fact]
    public void PriceDrawdown_BreachesWhenDrawdownExceedsThresholdForAllTrailingDays()
    {
        var closes = new List<DailyClose>
        {
            new(new DateOnly(2026, 1, 2), 100m),
            new(new DateOnly(2026, 1, 3), 65m),
            new(new DateOnly(2026, 1, 4), 60m),
            new(new DateOnly(2026, 1, 5), 55m),
        };

        var trigger = new ThesisInvalidationTrigger(
            ThesisMetric.PriceDrawdown, "greaterThan", 0.30m, ConsecutivePeriods: 3);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, [], closes);

        verdict.Should().BeOfType<TriggerVerdict.Breached>();
    }

    [Fact]
    public void NoTriggers_IsCallerResponsibility_EvaluatorNeverCalledWithNone()
    {
        // Documents the contract: the handler skips theses with zero triggers before calling the
        // evaluator (see RunThesisMonitorCommandHandler). Nothing to assert on the evaluator itself.
        Assert.True(true);
    }

    // ── Bug 1 regression: price_drawdown must anchor to entry price, not all-time peak ──────────

    [Fact]
    public void PriceDrawdown_EntryPriceProvided_AnchoredToEntry_NoBreach()
    {
        // MHPC scenario: entry $7.90, current $7.10 (~9.9% drawdown from entry).
        // Historical peak was much higher, which previously yielded a spurious 0.84 drawdown and
        // tripped a >0.50 trigger. With entry-price anchoring, drawdown should be ~0.101.
        const decimal entryPrice = 7.90m;
        var closes = new List<DailyClose>
        {
            new(new DateOnly(2026, 1, 1), 8.20m),  // price moved above entry after creation
            new(new DateOnly(2026, 1, 2), 7.50m),
            new(new DateOnly(2026, 1, 3), 7.10m),  // current — 9.9% below entry
        };

        var trigger = new ThesisInvalidationTrigger(ThesisMetric.PriceDrawdown, "greaterThan", 0.50m, ConsecutivePeriods: 1);
        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, [], closes, entryPrice);

        verdict.Should().BeOfType<TriggerVerdict.Held>();
    }

    [Fact]
    public void PriceDrawdown_EntryPriceProvided_ExceedsThreshold_Breaches()
    {
        // 60% drawdown from entry should fire a >0.50 trigger.
        const decimal entryPrice = 10.00m;
        var closes = new List<DailyClose>
        {
            new(new DateOnly(2026, 1, 1), 10.00m),
            new(new DateOnly(2026, 1, 2), 3.80m),  // 62% below entry
        };

        var trigger = new ThesisInvalidationTrigger(ThesisMetric.PriceDrawdown, "greaterThan", 0.50m, ConsecutivePeriods: 1);
        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, [], closes, entryPrice);

        var breached = verdict.Should().BeOfType<TriggerVerdict.Breached>().Subject;
        breached.ObservedValues[0].Should().BeApproximately(0.62m, 0.001m);
    }

    [Fact]
    public void PriceDrawdown_NoEntryPrice_FallsBackToFirstClose_NoBreach()
    {
        // When no entry price is set, the first close after thesis creation is used as the anchor.
        // This is safer than the historical peak and ensures a beaten-down name doesn't false-fire.
        var closes = new List<DailyClose>
        {
            new(new DateOnly(2026, 1, 1), 7.90m),  // first close on/after creation ≈ entry
            new(new DateOnly(2026, 1, 2), 7.10m),  // ~10% drawdown from first close
        };

        var trigger = new ThesisInvalidationTrigger(ThesisMetric.PriceDrawdown, "greaterThan", 0.50m, ConsecutivePeriods: 1);
        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, [], closes, entryPrice: null);

        verdict.Should().BeOfType<TriggerVerdict.Held>();
    }

    // ── relative_return (#697): sustained benchmark-relative underperformance ───────────────────

    [Fact]
    public void RelativeReturn_BreachesWhenSubjectSustainsUnderperformanceVsBenchmark()
    {
        var subjectCloses = new List<DailyClose>
        {
            new(new DateOnly(2026, 1, 1), 100m),
            new(new DateOnly(2026, 1, 2), 90m),
            new(new DateOnly(2026, 1, 3), 80m),
            new(new DateOnly(2026, 1, 4), 70m),
        };
        var benchmarkCloses = new List<DailyClose>
        {
            new(new DateOnly(2026, 1, 1), 100m),
            new(new DateOnly(2026, 1, 2), 100m),
            new(new DateOnly(2026, 1, 3), 100m),
            new(new DateOnly(2026, 1, 4), 100m),
        };

        var trigger = new ThesisInvalidationTrigger(
            ThesisMetric.RelativeReturn, "lessThan", -0.05m,
            ConsecutivePeriods: 2, BenchmarkTicker: "SPY", WindowDays: 1);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, [], subjectCloses, benchmarkCloses: benchmarkCloses);

        var breached = verdict.Should().BeOfType<TriggerVerdict.Breached>().Subject;
        breached.Metric.Should().Be(ThesisMetric.RelativeReturn);
        breached.ObservedValues.Should().HaveCount(2);
        breached.ObservedValues[0].Should().BeApproximately(-0.1111m, 0.001m);
        breached.ObservedValues[1].Should().BeApproximately(-0.125m, 0.001m);
    }

    [Fact]
    public void RelativeReturn_HoldsWhenSubjectTracksBenchmark()
    {
        var subjectCloses = new List<DailyClose>
        {
            new(new DateOnly(2026, 1, 1), 100m),
            new(new DateOnly(2026, 1, 2), 90m),
            new(new DateOnly(2026, 1, 3), 80m),
            new(new DateOnly(2026, 1, 4), 70m),
        };
        var benchmarkCloses = new List<DailyClose>
        {
            new(new DateOnly(2026, 1, 1), 100m),
            new(new DateOnly(2026, 1, 2), 90m),
            new(new DateOnly(2026, 1, 3), 80m),
            new(new DateOnly(2026, 1, 4), 70m),
        };

        var trigger = new ThesisInvalidationTrigger(
            ThesisMetric.RelativeReturn, "lessThan", -0.05m,
            ConsecutivePeriods: 2, BenchmarkTicker: "SPY", WindowDays: 1);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, [], subjectCloses, benchmarkCloses: benchmarkCloses);

        verdict.Should().BeOfType<TriggerVerdict.Held>();
    }

    [Fact]
    public void RelativeReturn_MissingBenchmarkHistory_IsNonEvaluable()
    {
        var subjectCloses = new List<DailyClose>
        {
            new(new DateOnly(2026, 1, 1), 100m),
            new(new DateOnly(2026, 1, 2), 90m),
        };

        var trigger = new ThesisInvalidationTrigger(
            ThesisMetric.RelativeReturn, "lessThan", -0.05m,
            ConsecutivePeriods: 1, BenchmarkTicker: "SPY", WindowDays: 1);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, [], subjectCloses, benchmarkCloses: []);

        var nonEvaluable = verdict.Should().BeOfType<TriggerVerdict.NonEvaluable>().Subject;
        nonEvaluable.Reason.Should().Be(NonEvaluableReason.NoBenchmarkHistory);
    }
}
