namespace FinanceSentry.Tests.Unit.Radar;

using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FluentAssertions;
using Xunit;

/// <summary>
/// Golden fixtures for the holdings-based price return (finance-framework R1(b)). Every expected value is
/// computed by hand in the fixture's comment: each step revalues the EARLIER capture's quantities at the
/// LATER capture's prices (price = value / quantity), divides by the earlier value, and the steps chain.
/// </summary>
public sealed class HoldingsBasedReturnCalculatorTests
{
    private static readonly DateTimeOffset Day0 = new(2026, 10, 1, 6, 0, 0, TimeSpan.Zero);

    private static HoldingsCapture At(int day, params CapturedHolding[] holdings) =>
        new(Day0.AddDays(day), holdings);

    private static CapturedHolding H(string symbol, decimal quantity, decimal usdValue) =>
        new(symbol, quantity, usdValue);

    // Day 0: AAA 10 @ 100 = 1,000.  Day 1: a 1,000 deposit buys BBB 5 @ 200; AAA is unchanged at 100.
    //   Value change: 1,000 -> 2,000 = +100%.
    //   Holdings-based: AAA 10 revalued at 100 = 1,000; BBB was not held at day 0, so it adds nothing.
    //   1,000 / 1,000 - 1 = 0%.
    [Fact]
    public void Deposit_WithNoPriceChange_ReadsZero()
    {
        var result = HoldingsBasedReturnCalculator.Measure([
            At(0, H("AAA", 10m, 1_000m)),
            At(1, H("AAA", 10m, 1_000m), H("BBB", 5m, 1_000m)),
        ]);

        result.Value.Should().Be(0m);
        result.Reason.Should().BeNull();
    }

    // Day 0: AAA 10 @ 100 = 1,000 (idle cash is not a position).  Day 1: 200 of idle cash buys CCC 4 @ 50.
    //   Value change: 1,000 -> 1,200 = +20%.
    //   Holdings-based: AAA revalued at 100 = 1,000; CCC adds nothing.  1,000 / 1,000 - 1 = 0%.
    [Fact]
    public void BuyFromCash_ReadsZero()
    {
        var result = HoldingsBasedReturnCalculator.Measure([
            At(0, H("AAA", 10m, 1_000m)),
            At(1, H("AAA", 10m, 1_000m), H("CCC", 4m, 200m)),
        ]);

        result.Value.Should().Be(0m);
    }

    // Day 0: AAA 10 @ 100 = 1,000.  Day 1: AAA 10 @ 110 = 1,100.
    //   Holdings-based: 10 x 110 = 1,100; 1,100 / 1,000 - 1 = +10%.
    [Fact]
    public void PriceMove_ReadsThatMove()
    {
        var result = HoldingsBasedReturnCalculator.Measure([
            At(0, H("AAA", 10m, 1_000m)),
            At(1, H("AAA", 10m, 1_100m)),
        ]);

        result.Value.Should().Be(0.1m);
    }

    // Day 0: AAA 10 @ 100 = 1,000.  Day 1: AAA 15 @ 110 = 1,650 (5 shares bought at 110).
    // Day 2: AAA 15 @ 99 = 1,485.
    //   Step 1: 10 x 110 = 1,100 over 1,000 = +10%.   Step 2: 15 x 99 = 1,485 over 1,650 = -10%.
    //   Chain: 1.10 x 0.90 = 0.99, so -1%.  (Value change would read 1,485 / 1,000 - 1 = +48.5%.)
    [Fact]
    public void Steps_ChainAcrossCaptures_AndIgnoreTheSharesBoughtBetween()
    {
        var result = HoldingsBasedReturnCalculator.Measure([
            At(0, H("AAA", 10m, 1_000m)),
            At(1, H("AAA", 15m, 1_650m)),
            At(2, H("AAA", 15m, 1_485m)),
        ]);

        result.Value.Should().Be(-0.01m);
    }

    // Captures arrive newest-first; the same chain as above must come out.
    [Fact]
    public void Captures_AreOrderedByTimeBeforeChaining()
    {
        var result = HoldingsBasedReturnCalculator.Measure([
            At(2, H("AAA", 15m, 1_485m)),
            At(0, H("AAA", 10m, 1_000m)),
            At(1, H("AAA", 15m, 1_650m)),
        ]);

        result.Value.Should().Be(-0.01m);
    }

    // Day 0: AAA 10 @ 100 = 1,000 and BBB 5 @ 200 = 1,000.  Day 1: BBB sold, AAA 10 @ 110 = 1,100.
    //   Start 2,000.  AAA revalued 1,100; BBB (gone) carried at its earlier 1,000.  End 2,100.
    //   2,100 / 2,000 - 1 = +5%.
    [Fact]
    public void SoldPosition_IsCarriedAtItsEarlierValue_ForTheStepThatSoldIt()
    {
        var result = HoldingsBasedReturnCalculator.Measure([
            At(0, H("AAA", 10m, 1_000m), H("BBB", 5m, 1_000m)),
            At(1, H("AAA", 10m, 1_100m)),
        ]);

        result.Value.Should().Be(0.05m);
    }

    // Day 0: AAA 10 @ 100 = 1,000.  Day 1: a 2-for-1 split, AAA 20 @ 50 = 1,000.
    //   Naive revaluation: 10 x 50 = 500, which would read -50%.  Value moved 0% while quantity doubled,
    //   so it is a reorganisation and is carried flat: 1,000 / 1,000 - 1 = 0%.
    [Fact]
    public void Split_IsNotReadAsALoss()
    {
        var result = HoldingsBasedReturnCalculator.Measure([
            At(0, H("AAA", 10m, 1_000m)),
            At(1, H("AAA", 20m, 1_000m)),
        ]);

        result.Value.Should().Be(0m);
    }

    // Day 0: AAA lots 6 @ 100 = 600 and 4 @ 100 = 400 (one symbol, two rows) -> 10 / 1,000.
    // Day 1: the same two lots at 120 = 720 and 480 -> 10 / 1,200.  1,200 / 1,000 - 1 = +20%.
    [Fact]
    public void RowsOfTheSameSymbol_AreSummed()
    {
        var result = HoldingsBasedReturnCalculator.Measure([
            At(0, H("AAA", 6m, 600m), H("AAA", 4m, 400m)),
            At(1, H("AAA", 6m, 720m), H("AAA", 4m, 480m)),
        ]);

        result.Value.Should().Be(0.2m);
    }

    // Day 0: AAA 10 @ 100 = 1,000 and BBB 5 held with no value (no price).  Day 1: both priced.
    //   BBB's start value is unknown, so no honest return exists: MissingPrice naming BBB.
    [Fact]
    public void HeldPosition_WithNoPriceAtTheEarlierCapture_YieldsMissingPrice()
    {
        var result = HoldingsBasedReturnCalculator.Measure([
            At(0, H("AAA", 10m, 1_000m), H("BBB", 5m, 0m)),
            At(1, H("AAA", 10m, 1_100m), H("BBB", 5m, 500m)),
        ]);

        result.Value.Should().BeNull();
        result.Reason.Should().Be(HoldingsReturnUnavailableReason.MissingPrice);
        result.MissingPriceSymbols.Should().BeEquivalentTo(["BBB"]);
    }

    // Day 0: AAA 10 @ 100 and BBB 5 @ 200.  Day 1: BBB still held (5) but with no value.
    //   BBB cannot be revalued at the later prices: MissingPrice naming BBB, not a number built from AAA alone.
    [Fact]
    public void HeldPosition_WithNoPriceAtTheLaterCapture_YieldsMissingPrice()
    {
        var result = HoldingsBasedReturnCalculator.Measure([
            At(0, H("AAA", 10m, 1_000m), H("BBB", 5m, 1_000m)),
            At(1, H("AAA", 10m, 1_100m), H("BBB", 5m, 0m)),
        ]);

        result.Value.Should().BeNull();
        result.Reason.Should().Be(HoldingsReturnUnavailableReason.MissingPrice);
        result.MissingPriceSymbols.Should().BeEquivalentTo(["BBB"]);
    }

    // A missing price anywhere in the window withholds the whole figure, even when the other steps chain.
    [Fact]
    public void MissingPrice_InAnyStep_WithholdsTheWholeWindow()
    {
        var result = HoldingsBasedReturnCalculator.Measure([
            At(0, H("AAA", 10m, 1_000m)),
            At(1, H("AAA", 10m, 1_100m), H("BBB", 5m, 0m)),
            At(2, H("AAA", 10m, 1_210m), H("BBB", 5m, 0m)),
        ]);

        result.Value.Should().BeNull();
        result.Reason.Should().Be(HoldingsReturnUnavailableReason.MissingPrice);
    }

    [Fact]
    public void FewerThanTwoCaptures_YieldsInsufficientCaptures_NotZero()
    {
        HoldingsBasedReturnCalculator.Measure([]).Reason
            .Should().Be(HoldingsReturnUnavailableReason.InsufficientCaptures);
        var single = HoldingsBasedReturnCalculator.Measure([At(0, H("AAA", 10m, 1_000m))]);
        single.Value.Should().BeNull();
        single.Reason.Should().Be(HoldingsReturnUnavailableReason.InsufficientCaptures);
    }

    // Two captures but neither holds a position: nothing to revalue.
    [Fact]
    public void EmptyBook_YieldsNoInvestedBase_NotZero()
    {
        var result = HoldingsBasedReturnCalculator.Measure([At(0), At(1)]);

        result.Value.Should().BeNull();
        result.Reason.Should().Be(HoldingsReturnUnavailableReason.NoInvestedBase);
    }
}
