using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Modules.Risk.Tests;

public sealed class BookDrawdownCalculatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static HoldingSnapshot Snap(int day, string symbol, decimal qty, decimal usd, string sleeve = RiskSleeve.Brokerage)
        => new() { Symbol = symbol, Sleeve = sleeve, Quantity = qty, UsdValue = usd, CapturedAt = T0.AddDays(day) };

    private static BookPosition Live(string symbol, decimal qty, decimal usd, string sleeve = RiskSleeve.Brokerage)
        => new(symbol, sleeve, qty, usd, 0m);

    [Fact]
    public void Measure_NoHistory_IsNull()
    {
        BookDrawdownCalculator.Measure([], [Live("AAA", 10m, 1000m)]).Should().BeNull();
    }

    [Fact]
    public void Measure_PeakToCurrent_IsTheFallFromThePeak()
    {
        // 100 -> 120 (peak) -> 90: fall from the peak is 25%.
        var history = new[] { Snap(0, "AAA", 10m, 1000m), Snap(1, "AAA", 10m, 1200m) };

        var result = BookDrawdownCalculator.Measure(history, [Live("AAA", 10m, 900m)]);

        result.Should().Be(0.25m);
    }

    [Fact]
    public void Measure_RisingBook_HasNoDrawdown()
    {
        var history = new[] { Snap(0, "AAA", 10m, 1000m) };

        BookDrawdownCalculator.Measure(history, [Live("AAA", 10m, 1100m)]).Should().Be(0m);
    }

    [Fact]
    public void Measure_Withdrawal_IsNotALoss()
    {
        // Half the shares sold and the cash taken out: value halves, prices did not move.
        var history = new[] { Snap(0, "AAA", 10m, 1000m) };

        BookDrawdownCalculator.Measure(history, [Live("AAA", 5m, 500m)]).Should().Be(0m);
    }

    [Fact]
    public void Measure_DepositIntoExistingHolding_IsNotAGain()
    {
        // Peak, then fall, then a deposit that buys more units: the deposit must not hide the fall.
        var history = new[] { Snap(0, "AAA", 10m, 1000m), Snap(1, "AAA", 10m, 800m) };

        var result = BookDrawdownCalculator.Measure(history, [Live("AAA", 20m, 1600m)]);

        result.Should().Be(0.2m);
    }

    [Fact]
    public void Measure_SoldOutPosition_StopsContributing()
    {
        var history = new[]
        {
            Snap(0, "AAA", 10m, 1000m), Snap(0, "BBB", 10m, 1000m),
            Snap(1, "AAA", 10m, 1000m), Snap(1, "BBB", 10m, 500m),
        };

        // BBB lost half, then is gone; the remaining book is flat from there.
        var result = BookDrawdownCalculator.Measure(history, [Live("AAA", 10m, 1000m)]);

        result.Should().Be(0.25m);
    }

    [Fact]
    public void Measure_StockSplit_IsReadAsFlat()
    {
        // 2-for-1: quantity doubles, value unchanged. Not a 50% price drop.
        var history = new[] { Snap(0, "AAA", 10m, 1000m) };

        BookDrawdownCalculator.Measure(history, [Live("AAA", 20m, 1000m)]).Should().Be(0m);
    }

    [Fact]
    public void Measure_SameInstantRowsAreOneCapture_AcrossSleeves()
    {
        var history = new[]
        {
            Snap(0, "AAA", 10m, 1000m), Snap(0, "BTC", 1m, 1000m, RiskSleeve.Crypto),
        };

        var result = BookDrawdownCalculator.Measure(history, [Live("AAA", 10m, 1000m), Live("BTC", 1m, 500m, RiskSleeve.Crypto)]);

        result.Should().Be(0.25m);
    }
}
