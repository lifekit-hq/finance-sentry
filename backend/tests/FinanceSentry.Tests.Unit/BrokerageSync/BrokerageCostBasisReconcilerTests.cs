namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FluentAssertions;
using Xunit;

/// <summary>
/// fs-688: cost basis is recomputed independently from persisted fills and reconciled against the
/// stored figure. Synthetic fills only.
/// </summary>
public class BrokerageCostBasisReconcilerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly BrokerageCostBasisReconciler _sut = new();

    private static BrokerageHolding Holding(string symbol, decimal quantity, decimal? averageCostUsd) =>
        new(UserId, symbol, "STK", quantity, quantity * 200m, "ibkr", averageCostUsd);

    private static BrokerageTrade Fill(
        string symbol,
        decimal quantity,
        decimal price,
        decimal? commission = null,
        string currency = "USD",
        int minute = 0,
        string execId = "exec") =>
        new(
            UserId,
            "ibkr",
            ibExecutionId: $"{execId}-{minute}",
            ibTradeId: $"{execId}-{minute}",
            conid: null,
            isin: null,
            symbol: symbol,
            openCloseIndicator: quantity > 0 ? "O" : "C",
            openDateTime: null,
            tradeDateTime: T0.AddMinutes(minute),
            quantity: quantity,
            price: price,
            proceeds: -(quantity * price),
            costBasis: null,
            realizedPnl: null,
            commission: commission,
            commissionCurrency: currency,
            taxes: null,
            currency: currency,
            fxRateToBase: null);

    [Fact]
    public void NoFillsAtAll_IsUnknown_WithFullyUntrackedQuantity()
    {
        var holding = Holding("AAPL", 10m, averageCostUsd: 150m);

        var result = _sut.Reconcile(holding, []);

        result.State.Should().Be(BasisState.Unknown);
        result.UntrackedQuantity.Should().Be(10m);
        result.RecomputedCostBasisUsd.Should().BeNull();
    }

    [Fact]
    public void FillsCoverTheFullQuantity_AndMatchStoredBasis_IsVerified()
    {
        var holding = Holding("AAPL", 10m, averageCostUsd: 150m); // stored CostBasisUsd = 1500

        var result = _sut.Reconcile(holding, [Fill("AAPL", 10m, 150m)]);

        result.State.Should().Be(BasisState.Verified);
        result.RecomputedCostBasisUsd.Should().Be(1500m);
        result.RecomputedQuantity.Should().Be(10m);
        result.UntrackedQuantity.Should().Be(0m);
    }

    [Fact]
    public void FillsCoverTheFullQuantity_ButDisagreeWithStoredBasis_IsUnverified()
    {
        var holding = Holding("AAPL", 10m, averageCostUsd: 200m); // stored CostBasisUsd = 2000

        // Fills alone reconstruct a cost basis of 1500 — far outside tolerance of the stored 2000.
        var result = _sut.Reconcile(holding, [Fill("AAPL", 10m, 150m)]);

        result.State.Should().Be(BasisState.Unverified);
        result.RecomputedCostBasisUsd.Should().Be(1500m);
    }

    [Fact]
    public void HeldQuantityExceedsWhatFillsExplain_IsUnknown()
    {
        // 15 held, but fills only ever bought 10 — 5 arrived some other way (transfer-in,
        // or history predating persistence).
        var holding = Holding("AAPL", 15m, averageCostUsd: 150m);

        var result = _sut.Reconcile(holding, [Fill("AAPL", 10m, 150m)]);

        result.State.Should().Be(BasisState.Unknown);
        result.UntrackedQuantity.Should().Be(5m);
    }

    [Fact]
    public void PartialSell_LeavesWeightedAverageCostOnTheRemainder()
    {
        // Buy 20 @ 100, sell 10 (weighted-average cost of the sold lot is 100), 10 remain at cost 1000.
        var holding = Holding("AAPL", 10m, averageCostUsd: 100m); // stored CostBasisUsd = 1000

        var result = _sut.Reconcile(
            holding,
            [
                Fill("AAPL", 20m, 100m, minute: 0),
                Fill("AAPL", -10m, 130m, minute: 1),
            ]);

        result.State.Should().Be(BasisState.Verified);
        result.RecomputedCostBasisUsd.Should().Be(1000m);
        result.RecomputedQuantity.Should().Be(10m);
    }

    [Fact]
    public void CommissionOnTheOpeningFill_IsAddedToCostBasis()
    {
        // Buy 10 @ 150 with a $5 commission (stored negative, IBKR convention) -> cost basis 1505.
        var holding = Holding("AAPL", 10m, averageCostUsd: 150.5m); // stored CostBasisUsd = 1505

        var result = _sut.Reconcile(holding, [Fill("AAPL", 10m, 150m, commission: -5m)]);

        result.State.Should().Be(BasisState.Verified);
        result.RecomputedCostBasisUsd.Should().Be(1505m);
    }

    [Fact]
    public void NativeCurrencyFills_AreConvertedToUsdAtTheReaderBoundary()
    {
        // 10 @ 100 EUR, fallback rate EUR=1.08 -> recomputed cost basis is 1080 USD.
        var holding = Holding("AAPL", 10m, averageCostUsd: 108m); // stored CostBasisUsd = 1080

        var result = _sut.Reconcile(holding, [Fill("AAPL", 10m, 100m, currency: "EUR")]);

        result.State.Should().Be(BasisState.Verified);
        result.RecomputedCostBasisUsd.Should().Be(1080m);
    }

    [Fact]
    public void HoldingWithNoStoredCostBasisAtAll_IsUnknown_EvenWithFullFillHistory()
    {
        var holding = Holding("AAPL", 10m, averageCostUsd: null);

        var result = _sut.Reconcile(holding, [Fill("AAPL", 10m, 150m)]);

        result.State.Should().Be(BasisState.Unknown);
        result.RecomputedCostBasisUsd.Should().Be(1500m);
    }

    [Fact]
    public void FillsForADifferentSymbol_AreIgnored()
    {
        var holding = Holding("AAPL", 10m, averageCostUsd: 150m);

        var result = _sut.Reconcile(holding, [Fill("MSFT", 10m, 150m)]);

        result.State.Should().Be(BasisState.Unknown);
        result.UntrackedQuantity.Should().Be(10m);
    }
}
