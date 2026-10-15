namespace FinanceSentry.Tests.Unit.Core.Measures;

using FinanceSentry.Core.Measures;
using FluentAssertions;
using Xunit;
using static FinanceSentry.Tests.Unit.Core.Measures.MeasureFixture;

/// <summary>The engine's contract outside the golden fixtures: M9, scope changes, and typed failures on bad input.</summary>
public class MeasureContractTests
{
    private static readonly ValuePoint[] WorkedExampleValues =
    [
        Close(Day(2026, 12, 31), 10000m),
        Close(Day(2027, 6, 30), 11000m),
        Close(Day(2027, 12, 31), 19800m),
    ];

    private static readonly ClassifiedFlow[] WorkedExampleFlows = [In(Day(2027, 7, 1), 10000m)];

    [Fact]
    public void Excess_WorkedExampleAgainstABenchmarkUpSevenPercent()
    {
        var book = TimeWeightedReturn.Measure(WorkedExampleValues, WorkedExampleFlows);

        var result = ExcessReturn.Measure(book, Close(Day(2026, 12, 31), 400m), Close(Day(2027, 12, 31), 428m));

        // Oracle: hand. Benchmark total-return index 400 → 428: 7%. Book TWR 13/350 (fixture 1).
        // Excess 13/350 - 7/100 = -23/700 = -3.2857%.
        result.BenchmarkReturn.Value.Should().Be(0.07m);
        result.Excess.Value!.Value.Should().BeApproximately(-23m / 700m, Exact);
    }

    [Fact]
    public void Excess_BenchmarkOverADifferentWindow_FailsWindowMismatch()
    {
        var book = TimeWeightedReturn.Measure(WorkedExampleValues, WorkedExampleFlows);

        var result = ExcessReturn.Measure(book, Close(Day(2027, 1, 4), 400m), Close(Day(2027, 12, 31), 428m));

        result.BenchmarkReturn.Failure.Should().Be(MeasureFailure.WindowMismatch);
        result.Excess.Failure.Should().Be(MeasureFailure.WindowMismatch);
    }

    [Fact]
    public void Excess_BookWithoutAReturn_PropagatesTheBooksReason()
    {
        var book = TimeWeightedReturn.Measure(
            [Close(Day(2026, 12, 31), 0m), Close(Day(2027, 12, 31), 100m)], [In(Day(2027, 1, 1), 100m)]);

        var result = ExcessReturn.Measure(book, Close(Day(2026, 12, 31), 400m), Close(Day(2027, 12, 31), 428m));

        result.Excess.Failure.Should().Be(MeasureFailure.ZeroBase);
    }

    [Fact]
    public void ValueChange_ScopeChanged_KeepsTheMoneyDropsThePercent()
    {
        var result = ValueChange.Measure(10000m, 15000m, scopeChanged: true);

        result.ChangeUsd.Should().Be(5000m);
        result.Percent.Failure.Should().Be(MeasureFailure.ScopeChanged);
    }

    [Fact]
    public void ValueChange_NegativeStart_FailsNegativeBase()
        => ValueChange.Measure(-500m, 100m, scopeChanged: false).Percent.Failure.Should().Be(MeasureFailure.NegativeBase);

    [Fact]
    public void Bridge_NewlyLinkedAccount_IsItsOwnLineNotGrowth()
    {
        // A 5,000 account linked mid-window; the existing 10,000 earned 100.
        var result = NetWorthBridge.Measure(10000m, 15100m, [], new BridgePositionLines(100m, 0m, 0m, 5000m, 0m)).Value!;

        result.NewlyLinkedUsd.Should().Be(5000m);
        result.InvestmentReturnUsd.Should().Be(100m);
        result.ResidualUsd.Should().Be(0m);
    }

    [Fact]
    public void Contributions_NegativeFlowAmount_FailsInvalidInput()
        => ContributionsAndGrowth.Measure(100m, 100m, [In(Day(2027, 1, 1), -5m)])
            .Failure.Should().Be(MeasureFailure.InvalidInput);

    [Fact]
    public void TimeWeighted_DuplicateValuationDate_FailsInvalidInput()
        => TimeWeightedReturn.Measure([Close(Day(2027, 1, 1), 100m), Close(Day(2027, 1, 1), 101m)], [])
            .Return.Failure.Should().Be(MeasureFailure.InvalidInput);

    [Fact]
    public void TimeWeighted_SingleValuation_FailsInsufficientData()
        => TimeWeightedReturn.Measure([Close(Day(2027, 1, 1), 100m)], [])
            .Return.Failure.Should().Be(MeasureFailure.InsufficientData);

    [Fact]
    public void MoneyWeighted_ClosingBeforeOpening_FailsInsufficientData()
        => MoneyWeightedReturn.Measure(Close(Day(2027, 2, 1), 100m), Close(Day(2027, 1, 1), 100m), [])
            .Return.Failure.Should().Be(MeasureFailure.InsufficientData);

    [Fact]
    public void GainVsCost_NonPositiveSplitRatio_FailsInvalidInput()
        => GainVsCost.Measure(
                [new(Day(2027, 1, 1), CostEventKind.Buy, Quantity: 1m, AmountUsd: 10m), new(Day(2027, 1, 2), CostEventKind.Split, Ratio: 0m)],
                10m)
            .Failure.Should().Be(MeasureFailure.InvalidInput);

    [Fact]
    public void GainVsCost_SellingEverything_LeavesNoCostAndNoAverage()
    {
        var result = GainVsCost.Measure(
            [
                new(Day(2027, 1, 1), CostEventKind.Buy, Quantity: 3m, AmountUsd: 100m),
                new(Day(2027, 2, 1), CostEventKind.Sell, Quantity: 3m, AmountUsd: 130m),
                new(Day(2027, 2, 2), CostEventKind.Income, AmountUsd: 2m),
            ],
            0m).Value!;

        // Oracle: hand. 3 units at 100/3 each, all sold for 130: realized 30, nothing left; plus 2 income.
        result.CostBasisUsd.Should().Be(0m);
        result.AverageCostUsd.Should().BeNull();
        result.RealizedUsd.Should().Be(30m);
        result.TotalGainUsd.Should().Be(32m);
    }
}
