using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync;

/// <summary>
/// The forward ledger (#472): cost basis from the connect date on, and never a guessed price for a
/// lot the venue did not show.
/// </summary>
public class ForwardCostBasisLedgerTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly ForwardCostBasisLedger _sut = new();

    private static CryptoTrade Buy(decimal quantity, decimal price, int minute = 0) =>
        new($"b{minute}-{quantity}", "BTC", "USD", quantity, price, quantity * price, IsBuyer: true, T0.AddMinutes(minute));

    private static CryptoTrade Sell(decimal quantity, decimal price, int minute = 0) =>
        new($"s{minute}-{quantity}", "BTC", "USD", quantity, price, quantity * price, IsBuyer: false, T0.AddMinutes(minute));

    private ForwardLedgerState Apply(ForwardLedgerState state, decimal current, params CryptoTrade[] trades) =>
        _sut.Apply(state, trades, current, reconcile: true);

    [Fact]
    public void BuysOnly_FromAnEmptyPosition_GiveAKnownWeightedAverage()
    {
        var state = Apply(ForwardLedgerState.Empty, 3m, Buy(1m, 100m, 1), Buy(2m, 130m, 2));

        state.CostBasisUsd.Should().Be(360m);
        state.AverageBuyPriceUsd.Should().Be(120m);
        state.UntrackedQuantity.Should().Be(0m);
        state.TradeCount.Should().Be(2);
        state.LastTradeAt.Should().Be(T0.AddMinutes(2));
    }

    [Fact]
    public void PositionHeldAtConnect_IsUntracked_AndCostBasisIsNull()
    {
        var state = Apply(ForwardLedgerState.Empty, 5m);

        state.UntrackedQuantity.Should().Be(5m);
        state.CostBasisUsd.Should().BeNull();
        state.AverageBuyPriceUsd.Should().BeNull();
        state.RealizedPnlUsd.Should().Be(0m);
    }

    [Fact]
    public void Sell_WithOnlyKnownLots_RealizesAgainstTheAverage()
    {
        var state = Apply(ForwardLedgerState.Empty, 1m, Buy(2m, 100m, 1), Sell(1m, 150m, 2));

        state.RealizedPnlUsd.Should().Be(50m);
        state.CostBasisUsd.Should().Be(100m);
        state.AverageBuyPriceUsd.Should().Be(100m);
    }

    [Fact]
    public void Sell_WhileUnpricedLotsAreHeld_RealizesNothing_AndShrinksBothPoolsInProportion()
    {
        // 0.5 held before connect (cost unknown), 0.1 bought at 60k, then 0.2 sold.
        var state = Apply(ForwardLedgerState.Empty, 0.4m, Buy(0.1m, 60_000m, 1), Sell(0.2m, 70_000m, 2));

        state.RealizedPnlUsd.Should().Be(0m, "the average cost of the lots sold is unknown");
        state.UntrackedQuantity.Should().BeApproximately(0.5m * 0.4m / 0.6m, 0.0000000001m);
        state.TrackedQuantity.Should().BeApproximately(0.1m * 0.4m / 0.6m, 0.0000000001m);
        state.TrackedCostUsd.Should().BeApproximately(6_000m * 0.4m / 0.6m, 0.0001m);
        state.CostBasisUsd.Should().BeNull();
    }

    [Fact]
    public void ClosingThePosition_ClearsTheUnknownLots_SoTheNextBuyStartsClean()
    {
        var closed = Apply(ForwardLedgerState.Empty, 0m, Sell(0.5m, 70_000m, 1));
        closed.Should().BeEquivalentTo(new { TrackedQuantity = 0m, UntrackedQuantity = 0m, RealizedPnlUsd = 0m });

        var reopened = Apply(closed, 0.2m, Buy(0.2m, 50_000m, 2));

        reopened.CostBasisUsd.Should().Be(10_000m);
        reopened.AverageBuyPriceUsd.Should().Be(50_000m);
        reopened.TradeCount.Should().Be(2);
    }

    [Fact]
    public void Deposit_BetweenWalks_IsUntracked_AndHidesTheCostBasis()
    {
        var bought = Apply(ForwardLedgerState.Empty, 1m, Buy(1m, 100m, 1));

        var afterDeposit = Apply(bought, 1.5m);

        afterDeposit.UntrackedQuantity.Should().Be(0.5m);
        afterDeposit.CostBasisUsd.Should().BeNull();
    }

    [Fact]
    public void Withdrawal_OrAFeeTakenInTheAsset_ShrinksTheKnownPool_WithoutLosingTheAverage()
    {
        var bought = Apply(ForwardLedgerState.Empty, 1m, Buy(1m, 100m, 1));

        var afterWithdrawal = Apply(bought, 0.999m);

        afterWithdrawal.UntrackedQuantity.Should().Be(0m);
        afterWithdrawal.TrackedQuantity.Should().Be(0.999m);
        afterWithdrawal.CostBasisUsd.Should().Be(99.9m);
        afterWithdrawal.AverageBuyPriceUsd.Should().Be(100m);
    }

    [Fact]
    public void SellBeforeAnyKnownLot_IsNotRealized()
    {
        var state = Apply(ForwardLedgerState.Empty, 1m, Sell(1m, 100m, 1), Buy(1m, 90m, 2));

        state.RealizedPnlUsd.Should().Be(0m);
        state.TradeCount.Should().Be(2);
    }

    [Fact]
    public void AccumulatesAcrossWalks()
    {
        var first = Apply(ForwardLedgerState.Empty, 2m, Buy(2m, 100m, 1));
        var second = Apply(first, 1m, Sell(1m, 130m, 2));
        var third = Apply(second, 2m, Buy(1m, 160m, 3));

        third.RealizedPnlUsd.Should().Be(30m);
        third.CostBasisUsd.Should().Be(260m);
        third.AverageBuyPriceUsd.Should().Be(130m);
        third.TradeCount.Should().Be(3);
    }

    [Fact]
    public void WithoutReconcile_FillsApply_ButTheBalanceGapIsLeftAlone()
    {
        var state = _sut.Apply(ForwardLedgerState.Empty, [Buy(1m, 100m, 1)], currentQuantity: 3m, reconcile: false);

        state.TrackedQuantity.Should().Be(1m);
        state.UntrackedQuantity.Should().Be(0m);
        state.CostBasisUsd.Should().Be(100m);
    }

    [Fact]
    public void FillsAreAppliedInTimeOrder_WhateverOrderTheyArrive()
    {
        var state = Apply(ForwardLedgerState.Empty, 1m, Sell(1m, 150m, 2), Buy(2m, 100m, 1));

        state.RealizedPnlUsd.Should().Be(50m);
        state.LastTradeAt.Should().Be(T0.AddMinutes(2));
    }
}
