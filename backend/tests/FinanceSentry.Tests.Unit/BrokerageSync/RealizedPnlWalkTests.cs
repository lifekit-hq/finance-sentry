namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FluentAssertions;
using Xunit;

/// <summary>Issue 982: realized P&amp;L is an average-cost walk over persisted fills. Synthetic fills only.</summary>
public class RealizedPnlWalkTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static BrokerageTrade Fill(
        decimal quantity, decimal price, int day = 0, decimal? commission = null, string currency = "USD")
        => new(
            UserId, "ibkr", $"exec-{day}-{quantity}", $"trade-{day}", null, null, "ABC",
            quantity > 0 ? "O" : "C", null, T0.AddDays(day), quantity, price, -(quantity * price), null, null,
            commission, currency, null, currency, null);

    [Fact]
    public void BuyThenPartialSell_RealizesTheGainOverTheAverageCost()
    {
        var result = RealizedPnlWalk.Walk([Fill(10m, 100m, 0), Fill(-4m, 150m, 10)], heldQuantity: 6m, from: null);

        result.Explained.Should().BeTrue();
        result.SoldInWindow.Should().BeTrue();
        result.RealizedUsd.Should().Be(200m); // 4 x (150 - 100)
    }

    [Fact]
    public void TwoBuysAtDifferentPrices_ReleaseTheWeightedAverageCost()
    {
        var result = RealizedPnlWalk.Walk(
            [Fill(10m, 100m, 0), Fill(10m, 200m, 1), Fill(-10m, 180m, 2)], heldQuantity: 10m, from: null);

        result.RealizedUsd.Should().Be(300m); // 10 x (180 - 150)
    }

    [Fact]
    public void Commission_ReducesTheGain_AndRaisesTheCost()
    {
        // IBKR reports commission as a negative number.
        var result = RealizedPnlWalk.Walk(
            [Fill(10m, 100m, 0, commission: -1m), Fill(-10m, 110m, 1, commission: -1m)], heldQuantity: 0m, from: null);

        result.RealizedUsd.Should().Be(98m); // 1100 - 1 proceeds, over 1000 + 1 cost
    }

    [Fact]
    public void OnlySellsAfterFrom_AreCounted_ButEarlierOnesStillMoveTheCost()
    {
        var fills = new[] { Fill(10m, 100m, 0), Fill(-5m, 120m, 5), Fill(-5m, 130m, 20) };

        var result = RealizedPnlWalk.Walk(fills, heldQuantity: 0m, from: DateOnly.FromDateTime(T0.AddDays(10)));

        result.RealizedUsd.Should().Be(150m); // only the day-20 sell: 5 x (130 - 100)
    }

    [Fact]
    public void ASellOnTheFromDay_IsNotCounted_BecauseTheOpeningValueIncludesIt()
    {
        var fills = new[] { Fill(10m, 100m, 0), Fill(-5m, 120m, 10), Fill(-5m, 130m, 11) };

        var result = RealizedPnlWalk.Walk(fills, heldQuantity: 0m, from: DateOnly.FromDateTime(T0.AddDays(10)));

        result.RealizedUsd.Should().Be(150m); // the day-10 sell is the opening day's; only the day-11 sell: 5 x (130 - 100)
        result.SoldInWindow.Should().BeTrue();
    }

    [Fact]
    public void NoSellInTheWindow_IsExplainedWithNoSell()
    {
        var result = RealizedPnlWalk.Walk([Fill(10m, 100m, 0)], heldQuantity: 10m, from: null);

        result.Explained.Should().BeTrue();
        result.SoldInWindow.Should().BeFalse();
        result.RealizedUsd.Should().Be(0m);
    }

    [Fact]
    public void SellWithNothingOpen_IsNotExplained()
    {
        var result = RealizedPnlWalk.Walk([Fill(-3m, 150m, 0)], heldQuantity: 0m, from: null);

        result.Explained.Should().BeFalse();
    }

    [Fact]
    public void SellLargerThanWhatIsOpen_IsNotExplained()
    {
        var result = RealizedPnlWalk.Walk([Fill(2m, 100m, 0), Fill(-5m, 150m, 1)], heldQuantity: 0m, from: null);

        result.Explained.Should().BeFalse();
    }

    [Fact]
    public void HeldQuantityTheFillsDoNotReach_IsNotExplained()
    {
        // The fills leave 4 open but 6 are held: 2 arrived without a recorded acquisition, so the average cost is unknown.
        var result = RealizedPnlWalk.Walk([Fill(10m, 100m, 0), Fill(-6m, 150m, 1)], heldQuantity: 6m, from: null);

        result.Explained.Should().BeFalse();
    }

    [Fact]
    public void ADisposalWithNoPrice_ReleasesCostWithoutRealizingAGain()
    {
        var result = RealizedPnlWalk.Walk([Fill(10m, 100m, 0), Fill(-5m, 0m, 1)], heldQuantity: 5m, from: null);

        result.Explained.Should().BeTrue();
        result.SoldInWindow.Should().BeFalse();
        result.RealizedUsd.Should().Be(0m);
    }

    [Fact]
    public void UnknownCurrency_CarriesItsConversionStatus()
    {
        var result = RealizedPnlWalk.Walk(
            [Fill(10m, 100m, 0, currency: "ZZZ"), Fill(-10m, 110m, 1, currency: "ZZZ")], heldQuantity: 0m, from: null);

        result.Fx.Should().Be(FxStatus.Unknown1To1);
    }
}
