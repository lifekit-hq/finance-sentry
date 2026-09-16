using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class CostBasisCalculatorTests
{
    private static CryptoTrade Buy(decimal qty, decimal price, long id = 1, DateTime? when = null) =>
        new(id.ToString(System.Globalization.CultureInfo.InvariantCulture), "BTC", "USDT", qty, price, qty * price, true,
            when ?? new DateTime(2024, 1, (int)id, 0, 0, 0, DateTimeKind.Utc));

    private static CryptoTrade Sell(decimal qty, decimal price, long id, DateTime? when = null) =>
        new(id.ToString(System.Globalization.CultureInfo.InvariantCulture), "BTC", "USDT", qty, price, qty * price, false,
            when ?? new DateTime(2024, 1, (int)id, 0, 0, 0, DateTimeKind.Utc));

    private readonly CostBasisCalculator _sut = new();

    [Fact]
    public void Compute_EmptyTrades_ReturnsZeros()
    {
        var result = _sut.Compute([]);

        result.CostBasisUsd.Should().Be(0m);
        result.AverageBuyPriceUsd.Should().Be(0m);
        result.RemainingQuantity.Should().Be(0m);
        result.RealizedPnlUsd.Should().Be(0m);
        result.LastTradeAt.Should().BeNull();
        result.TradeCount.Should().Be(0);
    }

    [Fact]
    public void Compute_SingleBuy_TracksCostBasisAndAvgPrice()
    {
        var result = _sut.Compute([Buy(qty: 1m, price: 20_000m, id: 1)]);

        result.CostBasisUsd.Should().Be(20_000m);
        result.AverageBuyPriceUsd.Should().Be(20_000m);
        result.RemainingQuantity.Should().Be(1m);
        result.RealizedPnlUsd.Should().Be(0m);
        result.LastTradeAt.Should().Be(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        result.TradeCount.Should().Be(1);
    }

    [Fact]
    public void Compute_TwoBuys_UsesWeightedAverage()
    {
        var result = _sut.Compute([
            Buy(qty: 1m, price: 20_000m, id: 1),
            Buy(qty: 1m, price: 30_000m, id: 2),
        ]);

        result.CostBasisUsd.Should().Be(50_000m);
        result.AverageBuyPriceUsd.Should().Be(25_000m);
        result.TradeCount.Should().Be(2);
    }

    [Fact]
    public void Compute_BuyThenSell_RealizesPnl_AndReducesCostBasis()
    {
        var result = _sut.Compute([
            Buy(qty: 2m, price: 10_000m, id: 1),
            Sell(qty: 1m, price: 15_000m, id: 2),
        ]);

        result.RealizedPnlUsd.Should().Be(5_000m);
        result.CostBasisUsd.Should().Be(10_000m);
        result.AverageBuyPriceUsd.Should().Be(10_000m);
        result.RemainingQuantity.Should().Be(1m);
        result.TradeCount.Should().Be(2);
    }

    [Fact]
    public void Compute_PartialSell_DoesNotReturnHistoricalBuyNotionalAsRemainingCostBasis()
    {
        var result = _sut.Compute([
            Buy(qty: 100m, price: 10m, id: 1),
            Buy(qty: 100m, price: 20m, id: 2),
            Sell(qty: 170m, price: 25m, id: 3),
        ]);

        result.RemainingQuantity.Should().Be(30m);
        result.AverageBuyPriceUsd.Should().Be(15m);
        result.CostBasisUsd.Should().Be(450m);
        result.CostBasisUsd.Should().NotBe(3_000m);
    }

    [Fact]
    public void Compute_SellsMoreThanHeld_CapsAtRunningQuantity()
    {
        var result = _sut.Compute([
            Buy(qty: 1m, price: 10_000m, id: 1),
            Sell(qty: 5m, price: 12_000m, id: 2),
        ]);

        // Only 1 unit was held, so realized P&L is on 1 unit at (12000 - 10000) = 2000.
        result.RealizedPnlUsd.Should().Be(2_000m);
        result.CostBasisUsd.Should().Be(0m);
    }

    [Fact]
    public void Compute_SortsByTimestampAndId()
    {
        var result = _sut.Compute([
            Sell(qty: 1m, price: 15_000m, id: 2),
            Buy(qty: 2m, price: 10_000m, id: 1),
        ]);

        result.RealizedPnlUsd.Should().Be(5_000m);
    }

    [Fact]
    public void Compute_WithSeed_PicksUpFromPreviousState()
    {
        var seed = new CostBasisResult(
            CostBasisUsd: 10_000m,
            AverageBuyPriceUsd: 10_000m,
            RemainingQuantity: 1m,
            RealizedPnlUsd: 0m,
            LastTradeAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TradeCount: 1);

        var result = _sut.Compute([Sell(qty: 1m, price: 12_000m, id: 2)], seed);

        result.RealizedPnlUsd.Should().Be(2_000m);
        result.CostBasisUsd.Should().Be(0m);
        result.TradeCount.Should().Be(2);
    }
}
