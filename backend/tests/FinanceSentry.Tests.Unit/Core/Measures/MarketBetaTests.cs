namespace FinanceSentry.Tests.Unit.Core.Measures;

using FinanceSentry.Core.Measures;
using FluentAssertions;
using Xunit;

/// <summary>Beta is the slope of the scope's monthly returns on the benchmark's, over the months both have.</summary>
public class MarketBetaTests
{
    private static SubPeriodReturn Month(int month, decimal value)
        => new(new DateOnly(2026, month, 1).AddDays(-1), new DateOnly(2026, month, DateTime.DaysInMonth(2026, month)), value);

    [Fact]
    public void ScopeMovingTwiceTheBenchmark_HasBetaTwo()
    {
        var benchmark = new[] { Month(2, 0.01m), Month(3, -0.02m), Month(4, 0.03m), Month(5, 0.005m) };
        var scope = benchmark.Select(m => m with { Return = m.Return * 2 }).ToList();

        var beta = MarketBeta.Measure(scope, benchmark, out var observations);

        beta.Value.Should().Be(2m);
        observations.Should().Be(4);
    }

    [Fact]
    public void ScopeMovingAgainstTheBenchmark_HasNegativeBeta()
    {
        var benchmark = new[] { Month(2, 0.01m), Month(3, -0.02m), Month(4, 0.03m) };
        var scope = benchmark.Select(m => m with { Return = -m.Return }).ToList();

        MarketBeta.Measure(scope, benchmark, out _).Value.Should().Be(-1m);
    }

    [Fact]
    public void OnlySharedMonthsCount()
    {
        var benchmark = new[] { Month(2, 0.01m), Month(3, -0.02m), Month(4, 0.03m) };
        var scope = new[] { Month(3, -0.04m), Month(4, 0.06m), Month(7, 0.5m) };

        var beta = MarketBeta.Measure(scope, benchmark, out var observations);

        observations.Should().Be(2);
        beta.Value.Should().Be(2m);
    }

    [Fact]
    public void FewerThanTwoSharedMonths_IsNotEnoughHistoryNeverZero()
    {
        var beta = MarketBeta.Measure([Month(3, 0.01m)], [Month(3, 0.02m)], out var observations);

        beta.Value.Should().BeNull();
        beta.Failure.Should().Be(MeasureFailure.NotEnoughHistory);
        observations.Should().Be(1);
    }

    [Fact]
    public void BenchmarkThatDidNotMove_IsInvalidInputNeverZero()
    {
        var beta = MarketBeta.Measure(
            [Month(3, 0.01m), Month(4, 0.02m)], [Month(3, 0.01m), Month(4, 0.01m)], out _);

        beta.Value.Should().BeNull();
        beta.Failure.Should().Be(MeasureFailure.InvalidInput);
    }
}
