namespace FinanceSentry.Modules.Research.Tests.ThesisMonitor;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.ThesisMonitor;
using FluentAssertions;
using Xunit;

public class ThesisBreakEvaluatorNonEvaluableTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static FundamentalFact Fact(
        string ticker, string concept, decimal value, int fiscalYear, string fiscalPeriod, DateOnly periodEnd)
        => new(ticker, concept, concept, "USD", value, periodEnd, fiscalPeriod, fiscalYear, "10-Q");

    [Fact]
    public void MissingFundamentals_IsNonEvaluableNoFundamentals()
    {
        var trigger = new ThesisInvalidationTrigger(ThesisMetric.Revenue, "lessThan", 100m);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, [], []);

        var nonEvaluable = verdict.Should().BeOfType<TriggerVerdict.NonEvaluable>().Subject;
        nonEvaluable.Reason.Should().Be(NonEvaluableReason.NoFundamentals);
    }

    [Fact]
    public void NonUsdRevenue_AgainstAnAbsoluteThreshold_IsNonEvaluableCurrencyMismatch()
    {
        var facts = new List<FundamentalFact>
        {
            Fact("BABA", "Revenue", 236_000_000_000m, 2026, "3M", new DateOnly(2026, 6, 30)) with { Unit = "CNY" },
            Fact("BABA", "Revenue", 230_000_000_000m, 2026, "3M", new DateOnly(2026, 3, 31)) with { Unit = "CNY" },
        };

        var trigger = new ThesisInvalidationTrigger(
            ThesisMetric.Revenue, "lessThan", 25_000_000_000m, ConsecutivePeriods: 2);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, facts, []);

        var nonEvaluable = verdict.Should().BeOfType<TriggerVerdict.NonEvaluable>().Subject;
        nonEvaluable.Reason.Should().Be("currency_mismatch:CNY");
    }

    [Fact]
    public void NonUsdEps_AgainstAnAbsoluteThreshold_IsNonEvaluableCurrencyMismatch()
    {
        var facts = new List<FundamentalFact>
        {
            Fact("BABA", "DilutedEPS", 12m, 2026, "3M", new DateOnly(2026, 6, 30)) with { Unit = "CNY/shares" },
        };

        var trigger = new ThesisInvalidationTrigger(ThesisMetric.DilutedEps, "lessThan", 1m);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, facts, []);

        verdict.Should().BeOfType<TriggerVerdict.NonEvaluable>()
            .Which.Reason.Should().Be(NonEvaluableReason.CurrencyMismatch("CNY"));
    }

    [Fact]
    public void UsdRevenue_AgainstAnAbsoluteThreshold_StillEvaluates()
    {
        var facts = new List<FundamentalFact>
        {
            Fact("GRAB", "Revenue", 20_000_000_000m, 2026, "3M", new DateOnly(2026, 6, 30)),
            Fact("GRAB", "Revenue", 21_000_000_000m, 2026, "3M", new DateOnly(2026, 3, 31)),
        };

        var trigger = new ThesisInvalidationTrigger(
            ThesisMetric.Revenue, "lessThan", 25_000_000_000m, ConsecutivePeriods: 2);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, facts, []);

        verdict.Should().BeOfType<TriggerVerdict.Breached>();
    }

    [Fact]
    public void InsufficientPeriods_IsNonEvaluableInsufficientPeriods()
    {
        var facts = new List<FundamentalFact>
        {
            Fact("GRAB", "Revenue", 100m, 2026, "Q1", new DateOnly(2026, 3, 31)),
        };

        var trigger = new ThesisInvalidationTrigger(
            ThesisMetric.Revenue, "lessThan", 200m, ConsecutivePeriods: 2);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, facts, []);

        var nonEvaluable = verdict.Should().BeOfType<TriggerVerdict.NonEvaluable>().Subject;
        nonEvaluable.Reason.Should().Be(NonEvaluableReason.InsufficientPeriods);
    }

    [Fact]
    public void RevenueZeroDenominator_IsNonEvaluableDivideByZero()
    {
        var facts = new List<FundamentalFact>
        {
            Fact("GRAB", "GrossProfit", 10m, 2026, "Q1", new DateOnly(2026, 3, 31)),
            Fact("GRAB", "Revenue", 0m, 2026, "Q1", new DateOnly(2026, 3, 31)),
        };

        var trigger = new ThesisInvalidationTrigger(ThesisMetric.GrossMargin, "lessThan", 0.35m);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, facts, []);

        var nonEvaluable = verdict.Should().BeOfType<TriggerVerdict.NonEvaluable>().Subject;
        nonEvaluable.Reason.Should().Be(NonEvaluableReason.DivideByZero);
    }

    [Fact]
    public void UnsupportedMetric_IsNonEvaluableUnsupportedMetric()
    {
        var trigger = new ThesisInvalidationTrigger("not_a_real_metric", "lessThan", 1m);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, [], []);

        var nonEvaluable = verdict.Should().BeOfType<TriggerVerdict.NonEvaluable>().Subject;
        nonEvaluable.Reason.Should().Be(NonEvaluableReason.UnsupportedMetric);
    }

    [Fact]
    public void UnknownDirection_IsNonEvaluableInvalidDirection()
    {
        var trigger = new ThesisInvalidationTrigger(ThesisMetric.Revenue, "declines", 100m);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, [], []);

        var nonEvaluable = verdict.Should().BeOfType<TriggerVerdict.NonEvaluable>().Subject;
        nonEvaluable.Reason.Should().Be(NonEvaluableReason.InvalidDirection);
    }

    [Fact]
    public void NonPositiveConsecutivePeriods_IsNonEvaluableInvalidConsecutivePeriods()
    {
        var trigger = new ThesisInvalidationTrigger(
            ThesisMetric.Revenue, "lessThan", 100m, ConsecutivePeriods: 0);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, [], []);

        var nonEvaluable = verdict.Should().BeOfType<TriggerVerdict.NonEvaluable>().Subject;
        nonEvaluable.Reason.Should().Be(NonEvaluableReason.InvalidConsecutivePeriods);
    }

    [Fact]
    public void NoPriceHistory_IsNonEvaluableNoPriceHistory()
    {
        var trigger = new ThesisInvalidationTrigger(ThesisMetric.PriceDrawdown, "greaterThan", 0.3m);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, [], []);

        var nonEvaluable = verdict.Should().BeOfType<TriggerVerdict.NonEvaluable>().Subject;
        nonEvaluable.Reason.Should().Be(NonEvaluableReason.NoPriceHistory);
    }

    [Fact]
    public void RelativeReturn_MissingBenchmarkTicker_IsNonEvaluableMissingBenchmarkConfiguration()
    {
        var trigger = new ThesisInvalidationTrigger(ThesisMetric.RelativeReturn, "lessThan", -0.05m, WindowDays: 63);

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, [], []);

        var nonEvaluable = verdict.Should().BeOfType<TriggerVerdict.NonEvaluable>().Subject;
        nonEvaluable.Reason.Should().Be(NonEvaluableReason.MissingBenchmarkConfiguration);
    }

    [Fact]
    public void RelativeReturn_MissingWindowDays_IsNonEvaluableMissingBenchmarkConfiguration()
    {
        var trigger = new ThesisInvalidationTrigger(ThesisMetric.RelativeReturn, "lessThan", -0.05m, BenchmarkTicker: "SPY");

        var verdict = ThesisBreakEvaluator.Evaluate(trigger, CreatedAt, [], []);

        var nonEvaluable = verdict.Should().BeOfType<TriggerVerdict.NonEvaluable>().Subject;
        nonEvaluable.Reason.Should().Be(NonEvaluableReason.MissingBenchmarkConfiguration);
    }

    [Theory]
    [InlineData(NonEvaluableReason.NoFundamentals)]
    [InlineData(NonEvaluableReason.InsufficientPeriods)]
    [InlineData(NonEvaluableReason.DivideByZero)]
    [InlineData(NonEvaluableReason.UnsupportedMetric)]
    [InlineData(NonEvaluableReason.NoPriceHistory)]
    [InlineData(NonEvaluableReason.MissingBenchmarkConfiguration)]
    [InlineData(NonEvaluableReason.NoBenchmarkHistory)]
    public void NonEvaluableVerdicts_AreNeverBreached(string reason)
    {
        TriggerVerdict verdict = new TriggerVerdict.NonEvaluable(reason);
        verdict.Should().NotBeOfType<TriggerVerdict.Breached>();
    }
}
