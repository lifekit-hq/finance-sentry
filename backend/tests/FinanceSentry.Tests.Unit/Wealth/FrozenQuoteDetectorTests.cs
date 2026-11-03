namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Services;
using FluentAssertions;
using Xunit;

public class FrozenQuoteDetectorTests
{
    // 2027-03-01 is a Monday.
    private static readonly DateOnly Monday = new(2027, 3, 1);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Friday = Monday.AddDays(-3);
    private static readonly DateOnly Saturday = Monday.AddDays(-2);
    private static readonly DateOnly Sunday = Monday.AddDays(-1);

    private static BrokerageHoldingSummary Holding(
        string symbol, decimal pricePerShare, decimal quantity = 10m, string type = "STK", string provider = "ibkr")
        => new(symbol, type, quantity, pricePerShare * quantity, DateTime.UtcNow, provider);

    private static DailySymbolPrice Price(string symbol, DateOnly date, decimal price) => new(symbol, date, price);

    /// <summary>The weekdays before <paramref name="today"/>, newest first.</summary>
    private static IEnumerable<DateOnly> WeekdaysBefore(DateOnly today)
    {
        for (var day = today.AddDays(-1); ; day = day.AddDays(-1))
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                yield return day;
    }

    /// <summary>One price per weekday before <paramref name="today"/>, oldest first, drawn from <paramref name="prices"/> (newest first).</summary>
    private static List<DailySymbolPrice> History(string symbol, DateOnly today, params decimal[] prices)
        => WeekdaysBefore(today).Zip(prices, (day, price) => Price(symbol, day, price)).Reverse().ToList();

    [Fact]
    public void LiquidSymbolPricedTheSameAsOnThePreviousWeekday_IsFrozen()
    {
        var history = new[] { Price("AAA", Friday, 12.5m) };

        FrozenQuoteDetector.HasFrozenLiquidQuote([Holding("AAA", 12.5m)], history, Monday).Should().BeTrue();
    }

    [Fact]
    public void PriceThatMovedSincethePreviousWeekday_IsNotFrozen()
    {
        var history = new[] { Price("AAA", Friday, 12.5m) };

        FrozenQuoteDetector.HasFrozenLiquidQuote([Holding("AAA", 12.6m)], history, Monday).Should().BeFalse();
    }

    [Fact]
    public void PriceRoundingNoiseFromDividingValueByQuantity_StillCountsAsUnchanged()
    {
        // 100.00 / 3 recomputed from a value rounded at another scale differs in the last digits only.
        var history = new[] { Price("AAA", Friday, 33.333333m) };
        var holding = new BrokerageHoldingSummary("AAA", "STK", 3m, 100m, DateTime.UtcNow, "ibkr");

        FrozenQuoteDetector.HasFrozenLiquidQuote([holding], history, Monday).Should().BeTrue();
    }

    [Theory]
    [InlineData(6)] // Saturday
    [InlineData(7)] // Sunday
    public void OnAWeekend_AnUnchangedPriceIsTheTruthNotAFreeze(int dayOfMarch)
    {
        var weekend = new DateOnly(2027, 3, dayOfMarch);
        var history = new[] { Price("AAA", weekend.AddDays(-dayOfMarch + 5), 12.5m) };

        FrozenQuoteDetector.HasFrozenLiquidQuote([Holding("AAA", 12.5m)], history, weekend).Should().BeFalse();
    }

    [Fact]
    public void WeekendCapturesAreIgnored_MondayIsComparedWithFriday()
    {
        // Friday differs from Monday; the Saturday and Sunday captures repeat Monday's price because the market was shut.
        var history = new[] { Price("AAA", Friday, 11m), Price("AAA", Saturday, 12.5m), Price("AAA", Sunday, 12.5m) };

        FrozenQuoteDetector.HasFrozenLiquidQuote([Holding("AAA", 12.5m)], history, Monday).Should().BeFalse();
    }

    [Fact]
    public void MondayAgainstAnUnchangedFriday_IsFrozenDespiteTheWeekendBetween()
    {
        var history = new[] { Price("AAA", Friday, 12.5m), Price("AAA", Saturday, 99m) };

        FrozenQuoteDetector.HasFrozenLiquidQuote([Holding("AAA", 12.5m)], history, Monday).Should().BeTrue();
    }

    [Fact]
    public void ACaptureFromTheSameDay_IsNotAPreviousSync()
    {
        var history = new[] { Price("AAA", Monday, 12.5m), Price("AAA", Friday, 11m) };

        FrozenQuoteDetector.HasFrozenLiquidQuote([Holding("AAA", 12.5m)], history, Monday).Should().BeFalse();
    }

    [Fact]
    public void ASymbolWithNoPriceHistory_CannotBeJudged()
    {
        FrozenQuoteDetector.HasFrozenLiquidQuote([Holding("AAA", 12.5m)], [], Tuesday).Should().BeFalse();
    }

    [Theory]
    [InlineData("CASH", "ibkr")]
    [InlineData("REIT", "ibkr")]
    [InlineData("BOND", "ibkr")]
    [InlineData("STK", "inzhur")]
    public void SymbolsThatAreNotExchangeQuotes_AreNeverJudged(string instrumentType, string provider)
    {
        var history = new[] { Price("AAA", Friday, 1m) };

        FrozenQuoteDetector.HasFrozenLiquidQuote([Holding("AAA", 1m, type: instrumentType, provider: provider)], history, Monday)
            .Should().BeFalse();
    }

    [Fact]
    public void PositionWithNoQuantity_IsNotJudged()
    {
        var history = new[] { Price("AAA", Friday, 0m) };
        var holding = new BrokerageHoldingSummary("AAA", "STK", 0m, 0m, DateTime.UtcNow, "ibkr");

        FrozenQuoteDetector.HasFrozenLiquidQuote([holding], history, Monday).Should().BeFalse();
    }

    [Fact]
    public void OneFrozenSymbolAmongMovingOnes_FlagsTheSleeve()
    {
        var history = new[] { Price("AAA", Friday, 10m), Price("BBB", Friday, 20m), Price("CCC", Friday, 30m) };
        var holdings = new[] { Holding("AAA", 10.5m), Holding("BBB", 20m), Holding("CCC", 29m) };

        FrozenQuoteDetector.HasFrozenLiquidQuote(holdings, history, Monday).Should().BeTrue();
    }

    [Fact]
    public void AllSymbolsMoving_IsNotFrozen()
    {
        var history = new[] { Price("AAA", Friday, 10m), Price("BBB", Friday, 20m) };
        var holdings = new[] { Holding("AAA", 10.5m), Holding("BBB", 19m) };

        FrozenQuoteDetector.HasFrozenLiquidQuote(holdings, history, Monday).Should().BeFalse();
    }

    [Fact]
    public void IlliquidSymbolThatOftenSitsOnOnePrice_IsNotJudged()
    {
        // Newest first: it repeated its price about half the time before today, so a repeat is its normal behaviour.
        var history = History("THIN", Monday, 5m, 5m, 5.1m, 5.1m, 5.2m, 5.2m, 5.3m, 5.3m, 5.4m, 5.4m, 5.5m, 5.5m);

        FrozenQuoteDetector.HasFrozenLiquidQuote([Holding("THIN", 5m)], history, Monday).Should().BeFalse();
    }

    [Fact]
    public void LiquidSymbolThatMovesEveryDay_IsFlaggedTheFirstTimeItRepeats()
    {
        var history = History("AAA", Monday, 5m, 5.1m, 5.2m, 5.3m, 5.4m, 5.5m, 5.6m, 5.7m, 5.8m, 5.9m, 6m, 6.1m);

        FrozenQuoteDetector.HasFrozenLiquidQuote([Holding("AAA", 5m)], history, Monday).Should().BeTrue();
    }

    [Fact]
    public void ALongFreezeDoesNotExcuseItself()
    {
        // Seven repeated days in a row after a long run of moving ones: the streak is left out of the symbol's record.
        var history = History("AAA", Monday, 5m, 5m, 5m, 5m, 5m, 5m, 5m, 6m, 6.1m, 6.2m, 6.3m, 6.4m, 6.5m, 6.6m, 6.7m);

        FrozenQuoteDetector.HasFrozenLiquidQuote([Holding("AAA", 5m)], history, Monday).Should().BeTrue();
    }

    [Fact]
    public void SymbolsAreMatchedWithoutRegardToCase()
    {
        var history = new[] { Price("aaa", Friday, 12.5m) };

        FrozenQuoteDetector.HasFrozenLiquidQuote([Holding("AAA", 12.5m)], history, Monday).Should().BeTrue();
    }
}
