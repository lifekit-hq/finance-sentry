namespace FinanceSentry.Tests.Unit.Core.Measures;

using FinanceSentry.Core.Measures;
using FluentAssertions;
using Xunit;
using static FinanceSentry.Tests.Unit.Core.Measures.MeasureFixture;

/// <summary>
/// Golden fixtures for M8 (finance-framework §3.4). Every expected value was computed outside the engine with
/// empyrical-reloaded 0.7.0 (numpy 2.5.4, pandas 3.0.6, Python 3.13, a throwaway virtualenv outside the repo):
/// <c>annual_volatility(r, period='monthly')</c>, <c>downside_risk(r, required_return=0, period='monthly')</c>
/// and <c>max_drawdown(r)</c> (negated here, the engine reports the fall as a positive fraction) on the monthly
/// returns written in each fixture. The engine is fed month-end values built from those returns, so the whole
/// path (month-ends, time-weighted chaining, statistics) is held to the library.
/// </summary>
public class RiskStatisticsTests
{
    private const double Tolerance = 0.000000001;

    // Fixture A: 12 monthly returns. empyrical: vol 0.07669301260100676, downside 0.05049752469181039,
    // max_drawdown -0.04045960000000026.
    private static readonly decimal[] FixtureA =
        [0.02m, -0.01m, 0.03m, -0.04m, 0.015m, 0.01m, -0.025m, 0.005m, 0.02m, -0.015m, 0.03m, 0.01m];

    // Fixture B: 36 monthly returns, round(((i * 7) % 11 - 5) / 200 + 0.004, 4) for i in 0..35.
    // empyrical: vol 0.05572679957144668, downside 0.031843366656181316, max_drawdown -0.021979000000000148.
    private static readonly decimal[] FixtureB =
    [
        -0.021m, 0.014m, -0.006m, 0.029m, 0.009m, -0.011m, 0.024m, 0.004m, -0.016m, 0.019m, -0.001m,
        -0.021m, 0.014m, -0.006m, 0.029m, 0.009m, -0.011m, 0.024m, 0.004m, -0.016m, 0.019m, -0.001m,
        -0.021m, 0.014m, -0.006m, 0.029m, 0.009m, -0.011m, 0.024m, 0.004m, -0.016m, 0.019m, -0.001m,
        -0.021m, 0.014m, -0.006m,
    ];

    [Fact]
    public void FixtureA_MatchesEmpyrical()
    {
        var result = RiskStatistics.Measure(MonthEnds(FixtureA), []);

        ((double)result.Volatility.Value!.Value).Should().BeApproximately(0.07669301260100676, Tolerance);
        ((double)result.DownsideDeviation.Value!.Value).Should().BeApproximately(0.05049752469181039, Tolerance);
        ((double)result.MaxDrawdown.Value!.Value).Should().BeApproximately(0.04045960000000026, Tolerance);
        result.MonthlyReturns.Select(m => m.Return).Should().Equal(FixtureA);
    }

    [Fact]
    public void FixtureB_ThirtySixMonths_MatchesEmpyricalAndCarriesTheThreeYearFigure()
    {
        var result = RiskStatistics.Measure(MonthEnds(FixtureB), []);

        ((double)result.Volatility.Value!.Value).Should().BeApproximately(0.05572679957144668, Tolerance);
        ((double)result.ThreeYearVolatility.Value!.Value).Should().BeApproximately(0.05572679957144668, Tolerance);
        ((double)result.DownsideDeviation.Value!.Value).Should().BeApproximately(0.031843366656181316, Tolerance);
        ((double)result.MaxDrawdown.Value!.Value).Should().BeApproximately(0.021979000000000148, Tolerance);
    }

    [Fact]
    public void ThirtyFiveMonths_ThreeYearFigureIsNotEnoughHistoryNotANumber()
    {
        var result = RiskStatistics.Measure(MonthEnds(FixtureB.Take(35).ToArray()), []);

        result.ThreeYearVolatility.Value.Should().BeNull();
        result.ThreeYearVolatility.Failure.Should().Be(MeasureFailure.NotEnoughHistory);
        result.Volatility.Value.Should().NotBeNull();
    }

    [Fact]
    public void MoreThanThirtySixMonths_ThreeYearFigureUsesTheLastThirtySix()
    {
        var returns = new[] { 0.5m }.Concat(FixtureB).ToArray();

        var result = RiskStatistics.Measure(MonthEnds(returns), []);

        ((double)result.ThreeYearVolatility.Value!.Value).Should().BeApproximately(0.05572679957144668, Tolerance);
        result.Volatility.Value!.Value.Should().BeGreaterThan(result.ThreeYearVolatility.Value!.Value);
    }

    [Fact]
    public void NoDownside_DownsideDeviationAndDrawdownAreZeroAsMeasuredNumbers()
    {
        // empyrical, returns [0.01, 0.02, 0.015, 0.01]: vol 0.016583123951777, downside 0.0, max_drawdown 0.0.
        var result = RiskStatistics.Measure(MonthEnds([0.01m, 0.02m, 0.015m, 0.01m]), []);

        ((double)result.Volatility.Value!.Value).Should().BeApproximately(0.016583123951777, Tolerance);
        result.DownsideDeviation.Value.Should().Be(0m);
        result.MaxDrawdown.Value.Should().Be(0m);
    }

    [Fact]
    public void FallInTheFirstMonth_CountsAgainstTheOpeningIndex()
    {
        // empyrical, returns [-0.05, 0.02, 0.02]: vol 0.14, downside 0.1, max_drawdown -0.05. The downside
        // averages over all three observations: sqrt(0.05² / 3) × √12 = 0.1, not the below-target-only 0.173.
        var result = RiskStatistics.Measure(MonthEnds([-0.05m, 0.02m, 0.02m]), []);

        ((double)result.Volatility.Value!.Value).Should().BeApproximately(0.14, Tolerance);
        ((double)result.DownsideDeviation.Value!.Value).Should().BeApproximately(0.1, Tolerance);
        ((double)result.MaxDrawdown.Value!.Value).Should().BeApproximately(0.05, Tolerance);
    }

    [Fact]
    public void DepositsAndWithdrawalsDoNotMoveAnyStatistic()
    {
        var values = MonthEnds(FixtureA);
        // A 10,000 deposit at the start of 15 Apr 2023 and a 3,000 withdrawal at the end of 20 Aug 2023: the
        // month-end values carry them, the time-weighted chain takes them out again.
        ClassifiedFlow[] flows = [In(Day(2023, 4, 15), 10000m), Out(Day(2023, 8, 20), 3000m)];
        var adjusted = MonthEndsWithFlows(FixtureA, flows);

        var withFlows = RiskStatistics.Measure(adjusted, flows);
        var without = RiskStatistics.Measure(values, []);

        ((double)withFlows.Volatility.Value!.Value).Should().BeApproximately((double)without.Volatility.Value!.Value, Tolerance);
        ((double)withFlows.DownsideDeviation.Value!.Value).Should().BeApproximately((double)without.DownsideDeviation.Value!.Value, Tolerance);
        ((double)withFlows.MaxDrawdown.Value!.Value).Should().BeApproximately((double)without.MaxDrawdown.Value!.Value, Tolerance);
    }

    [Fact]
    public void DrawdownIsSeenInsideAMonthThatEndsHigher()
    {
        // A daily close drops 10% mid-month and recovers by month-end: monthly returns are +1%, +1%, yet the
        // flow-neutral index fell 10%.
        List<ValuePoint> values =
        [
            Close(Day(2023, 1, 31), 1000m),
            Close(Day(2023, 2, 14), 900m),
            Close(Day(2023, 2, 28), 1010m),
            Close(Day(2023, 3, 31), 1020.1m),
        ];

        var result = RiskStatistics.Measure(values, []);

        result.MaxDrawdown.Value!.Value.Should().BeApproximately(0.1m, Exact);
    }

    [Fact]
    public void OneMonthlyReturn_VolatilityAndDownsideAreNotEnoughHistory()
    {
        var result = RiskStatistics.Measure(MonthEnds([0.02m]), []);

        result.Volatility.Failure.Should().Be(MeasureFailure.NotEnoughHistory);
        result.DownsideDeviation.Failure.Should().Be(MeasureFailure.NotEnoughHistory);
        result.Volatility.Value.Should().BeNull();
        result.MaxDrawdown.Value.Should().NotBeNull();
    }

    [Fact]
    public void FewerThanTwoValuations_EverythingIsInsufficientData()
    {
        var result = RiskStatistics.Measure([Close(Day(2023, 1, 31), 1000m)], []);

        new[] { result.Volatility, result.ThreeYearVolatility, result.DownsideDeviation, result.MaxDrawdown }
            .Should().OnlyContain(r => r.Value == null && r.Failure == MeasureFailure.InsufficientData);
    }

    [Fact]
    public void ZeroOpeningValue_NeverReadsAsAZeroRisk()
    {
        var result = RiskStatistics.Measure([Close(Day(2023, 1, 31), 0m), Close(Day(2023, 2, 28), 100m)], []);

        result.Volatility.Failure.Should().Be(MeasureFailure.ZeroBase);
        result.MaxDrawdown.Failure.Should().Be(MeasureFailure.ZeroBase);
    }

    [Fact]
    public void NegativeFlowAmount_IsInvalidInput()
    {
        var result = RiskStatistics.Measure(MonthEnds(FixtureA), [In(Day(2023, 3, 3), -1m)]);

        result.Volatility.Failure.Should().Be(MeasureFailure.InvalidInput);
        result.DownsideDeviation.Failure.Should().Be(MeasureFailure.InvalidInput);
    }

    [Fact]
    public void DuplicateValuationDate_IsInvalidInput()
    {
        var values = MonthEnds(FixtureA).Append(Close(Day(2023, 12, 31), 5m)).ToList();

        RiskStatistics.Measure(values, []).Volatility.Failure.Should().Be(MeasureFailure.InvalidInput);
    }

    [Fact]
    public void MonthWithNoValuation_IsMissingValueNotABridgedReturn()
    {
        List<ValuePoint> values =
        [
            Close(Day(2023, 1, 31), 1000m),
            Close(Day(2023, 2, 28), 1010m),
            Close(Day(2023, 4, 30), 1100m),
        ];

        var result = RiskStatistics.Measure(values, []);

        result.Volatility.Failure.Should().Be(MeasureFailure.MissingValue);
        result.MaxDrawdown.Value.Should().BeNull();
    }

    [Fact]
    public void ValueFromNothingWithoutAFlow_FailsTypedInsteadOfChainingAReturn()
    {
        List<ValuePoint> values =
        [
            Close(Day(2023, 1, 31), 1000m),
            Close(Day(2023, 2, 28), 0m),
            Close(Day(2023, 3, 31), 500m),
            Close(Day(2023, 4, 30), 550m),
        ];

        var result = RiskStatistics.Measure(values, []);

        // The March month opens at zero: it has no return, so no statistic is guessed from the others.
        result.Volatility.Failure.Should().Be(MeasureFailure.ZeroBase);
        result.MaxDrawdown.Value.Should().BeNull();
    }

    // Month-end closes that realise the given monthly returns from 1,000 at the close of 31 Dec 2022.
    private static List<ValuePoint> MonthEnds(decimal[] returns) => MonthEndsWithFlows(returns, []);

    // The same closes with external flows in the account's value: a month's value is the opening value plus
    // its inflows (placed at the start of their day), grown by the month's return, less its outflows (end of day).
    private static List<ValuePoint> MonthEndsWithFlows(decimal[] returns, ClassifiedFlow[] flows)
    {
        var value = 1000m;
        var date = Day(2022, 12, 31);
        List<ValuePoint> points = [Close(date, value)];
        foreach (var monthReturn in returns)
        {
            var next = new DateOnly(date.Year, date.Month, 1).AddMonths(2).AddDays(-1);
            var inMonth = flows.Where(f => f.Date > date && f.Date <= next).ToList();
            var inflows = inMonth.Where(f => f.Class == FlowClass.ExternalInflow).Sum(f => f.AmountUsd);
            var outflows = inMonth.Where(f => f.Class == FlowClass.ExternalOutflow).Sum(f => f.AmountUsd);
            value = ((value + inflows) * (1m + monthReturn)) - outflows;
            date = next;
            points.Add(Close(date, value));
        }

        return points;
    }
}
