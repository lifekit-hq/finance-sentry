namespace FinanceSentry.Tests.Unit.Core.Measures;

using FinanceSentry.Core.Measures;
using FluentAssertions;
using Xunit;
using static FinanceSentry.Tests.Unit.Core.Measures.MeasureFixture;

/// <summary>
/// Golden fixtures 11-20 (the crypto-flows scout's continuation of R5's list): the engine side only. Each
/// fixture's flows arrive already classified the way the scout's tables classify them; transfer matching,
/// spam filtering, price-confidence levels and the headline quality (COMPLETE / DEGRADED / ESTIMATED) belong
/// to the classification and headline work, not to the engine. Prices are constant within a fixture, so only
/// classification moves the numbers. Expected values are hand calculations, named above each assertion.
/// </summary>
public class CryptoFlowGoldenFixtureTests
{
    // Fixture 11: Binance sends 1 ETH (2,000) to linked wallet W with a 0.002 ETH (4) network fee, on 2 Mar
    // 2027. Opening at the close of 1 Mar: Binance 2 ETH (4,000), W empty.
    [Fact]
    public void Fixture11_UmbrellaScope_TransferIsInternalOnlyTheFeeIsGrowth()
    {
        var day = Day(2027, 3, 2);
        ClassifiedFlow[] flows = [Internal(day, 2000m), Fee(day, 4m)];

        var change = ValueChange.Measure(4000m, 3996m, scopeChanged: false);
        var contributions = ContributionsAndGrowth.Measure(4000m, 3996m, flows).Value!;
        var twr = TimeWeightedReturn.Measure([Close(Day(2027, 3, 1), 4000m), Close(day, 3996m)], flows);

        // Oracle: hand. 3,996 - 4,000 = -4, none of it put in or taken out; 3,996 / 4,000 - 1 = -0.10%.
        change.ChangeUsd.Should().Be(-4m);
        contributions.ContributionsUsd.Should().Be(0m);
        contributions.GrowthUsd.Should().Be(-4m);
        contributions.FeesUsd.Should().Be(4m);
        twr.Return.Value.Should().Be(-0.001m);
    }

    [Fact]
    public void Fixture11_BinanceOnlyScope_TransferIsAnOutflow()
    {
        var day = Day(2027, 3, 2);
        ClassifiedFlow[] flows = [Out(day, 2000m), Fee(day, 4m)];
        var opening = Close(Day(2027, 3, 1), 4000m);
        var closing = Close(day, 1996m);

        var change = ValueChange.Measure(4000m, 1996m, scopeChanged: false);
        var contributions = ContributionsAndGrowth.Measure(4000m, 1996m, flows).Value!;
        var twr = TimeWeightedReturn.Measure([opening, closing], flows);
        var dietz = MoneyWeightedReturn.Measure(opening, closing, flows);

        // Oracle: hand. Change -2,004 = -2,000 taken out - 4 fee; TWR (1,996 + 2,000) / 4,000 - 1 = -0.10%;
        // Modified Dietz -4 / 4,000 = -0.10% (the outflow leaves at the end of the only day: weight 0).
        change.ChangeUsd.Should().Be(-2004m);
        contributions.ContributionsUsd.Should().Be(-2000m);
        contributions.GrowthUsd.Should().Be(-4m);
        twr.Return.Value.Should().Be(-0.001m);
        dietz.Return.Value.Should().Be(-0.001m);
    }

    [Fact]
    public void Fixture11_WalletOnlyScope_ZeroBase()
    {
        var day = Day(2027, 3, 2);
        ClassifiedFlow[] flows = [In(day, 2000m)];

        var change = ValueChange.Measure(0m, 2000m, scopeChanged: false);
        var contributions = ContributionsAndGrowth.Measure(0m, 2000m, flows).Value!;
        var twr = TimeWeightedReturn.Measure([Close(Day(2027, 3, 1), 0m), Close(day, 2000m)], flows);

        // Oracle: hand. +2,000, all of it put in; no percent of a zero start.
        change.ChangeUsd.Should().Be(2000m);
        change.Percent.Failure.Should().Be(MeasureFailure.ZeroBase);
        contributions.ContributionsUsd.Should().Be(2000m);
        contributions.GrowthUsd.Should().Be(0m);
        twr.Return.Failure.Should().Be(MeasureFailure.ZeroBase);
    }

    // Fixture 12: 10 SOL (1,500) sent from Revolut X late on 3 Mar 2027 with a 0.05 SOL (7.50) fee, arriving on
    // Binance after midnight. Umbrella scope; opening 20 SOL (3,000) at the close of 2 Mar.
    [Fact]
    public void Fixture12_InTransitCounted_DailyReturnsShowOnlyTheFee()
    {
        ClassifiedFlow[] flows = [Internal(Day(2027, 3, 3), 1500m), Fee(Day(2027, 3, 3), 7.50m)];
        var twr = TimeWeightedReturn.Measure(
            [Close(Day(2027, 3, 2), 3000m), Close(Day(2027, 3, 3), 2992.50m), Close(Day(2027, 3, 4), 2992.50m)], flows);
        var contributions = ContributionsAndGrowth.Measure(3000m, 2992.50m, flows).Value!;

        // Oracle: hand. 3 Mar: 1,492.50 + 1,500 in transit = 2,992.50, 2,992.50 / 3,000 - 1 = -0.25%;
        // 4 Mar: 2,992.50 / 2,992.50 - 1 = 0. Growth -7.50, the fee.
        twr.Periods.Select(p => p.Return).Should().Equal(-0.0025m, 0m);
        twr.Return.Value.Should().Be(-0.0025m);
        contributions.ContributionsUsd.Should().Be(0m);
        contributions.GrowthUsd.Should().Be(-7.50m);
    }

    [Fact]
    public void Fixture12_InTransitDropped_ChainHidesTheDipButThePeriodsShowIt()
    {
        var twr = TimeWeightedReturn.Measure(
            [Close(Day(2027, 3, 2), 3000m), Close(Day(2027, 3, 3), 1492.50m), Close(Day(2027, 3, 4), 2992.50m)],
            [Internal(Day(2027, 3, 3), 1500m), Fee(Day(2027, 3, 3), 7.50m)]);

        // Oracle: hand. 3 Mar: 1,492.50 / 3,000 - 1 = -50.25%; 4 Mar: 2,992.50 / 1,492.50 - 1 = 200/199 (+100.50%);
        // chained 2,992.50 / 3,000 - 1 = -0.25%, the same. Only the per-period returns (what drawdown, M8,
        // reads) expose the missing in-transit value, which is why the engine returns them.
        twr.Periods[0].Return.Should().Be(-0.5025m);
        twr.Periods[1].Return.Should().BeApproximately(200m / 199m, Exact);
        twr.Return.Value!.Value.Should().BeApproximately(-0.0025m, Exact);
    }

    // Fixture 13: an unmatched 500 USDT deposit on Binance, 5 Mar 2027, classified external (provisionally or
    // on confirmation). Opening 1,000 at the close of 4 Mar.
    [Fact]
    public void Fixture13_UnmatchedDeposit_IsNeverGrowth()
    {
        ClassifiedFlow[] flows = [In(Day(2027, 3, 5), 500m)];
        var contributions = ContributionsAndGrowth.Measure(1000m, 1500m, flows).Value!;
        var twr = TimeWeightedReturn.Measure([Close(Day(2027, 3, 4), 1000m), Close(Day(2027, 3, 5), 1500m)], flows);

        // Oracle: hand. Contributions +500, growth 1,500 - 1,000 - 500 = 0; TWR 1,500 / (1,000 + 500) - 1 = 0.
        // A wrong default (the deposit left unclassified) would read growth +500.
        contributions.ContributionsUsd.Should().Be(500m);
        contributions.GrowthUsd.Should().Be(0m);
        twr.Return.Value.Should().Be(0m);
    }

    // Fixture 14: a 2,000 USDT → 1 ETH trade on 6 Mar 2027 with a 0.005 BNB (3.00) commission. Opening 2,600 at
    // the close of 5 Mar.
    [Fact]
    public void Fixture14_TradeFeeInAnotherAsset_LandsOnTheFeesLineNotMarket()
    {
        var day = Day(2027, 3, 6);
        ClassifiedFlow[] flows = [Internal(day, 2000m), Fee(day, 3m)];
        var contributions = ContributionsAndGrowth.Measure(2600m, 2597m, flows).Value!;
        var twr = TimeWeightedReturn.Measure([Close(Day(2027, 3, 5), 2600m), Close(day, 2597m)], flows);
        var bridge = NetWorthBridge.Measure(2600m, 2597m, flows, new BridgePositionLines(0m, 0m, 0m, 0m, 0m)).Value!;

        // Oracle: hand. M2 0 / -3; TWR 2,597 / 2,600 - 1 = -3/2600 = -0.1154%; bridge fees 3, market 0,
        // investment return -3, residual 0.
        contributions.ContributionsUsd.Should().Be(0m);
        contributions.GrowthUsd.Should().Be(-3m);
        contributions.MarketUsd.Should().Be(0m);
        twr.Return.Value!.Value.Should().BeApproximately(-3m / 2600m, Exact);
        bridge.FeesUsd.Should().Be(3m);
        bridge.InvestmentReturnUsd.Should().Be(-3m);
        bridge.ResidualUsd.Should().Be(0m);
    }

    [Fact]
    public void Fixture14_CommissionDropped_FeeReadsAsAMarketMove()
    {
        var result = ContributionsAndGrowth.Measure(2600m, 2597m, [Internal(Day(2027, 3, 6), 2000m)]).Value!;

        // Oracle: hand. With no fee recorded, the -3 has nowhere to go but market moves: the regression the
        // scout's fixture pins on today's adapter.
        result.FeesUsd.Should().Be(0m);
        result.MarketUsd.Should().Be(-3m);
    }

    // Fixture 15: a dust conversion on 7 Mar 2027: 2.50 of two tokens into 0.0049 BNB (2.45) with a 0.0001 BNB
    // (0.05) service charge.
    [Fact]
    public void Fixture15_DustConversion_OnlyTheServiceChargeIsGrowth()
    {
        var day = Day(2027, 3, 7);
        var result = ContributionsAndGrowth.Measure(
            2.50m, 2.45m, [Internal(day, 1.00m), Internal(day, 1.50m), Fee(day, 0.05m)]).Value!;

        // Oracle: hand. 2.45 - 2.50 = -0.05, all of it the fee: contributions 0, growth -0.05, market 0.
        result.ContributionsUsd.Should().Be(0m);
        result.GrowthUsd.Should().Be(-0.05m);
        result.MarketUsd.Should().Be(0m);
    }

    // Fixture 16: 1,000 USDT into flexible Earn on 1 Apr 2027, 30 daily rewards of 0.10, redeemed 1,003 on 30 Apr.
    // Opening 1,000 at the close of 31 Mar.
    [Fact]
    public void Fixture16_EarnRewards_AreIncomeAndTheWholeReturn()
    {
        var opening = Day(2027, 3, 31);
        var flows = new List<ClassifiedFlow> { Internal(Day(2027, 4, 1), 1000m), Internal(Day(2027, 4, 30), 1003m) };
        var values = new List<ValuePoint> { Close(opening, 1000m) };
        for (var d = 1; d <= 30; d++)
        {
            flows.Add(Income(opening.AddDays(d), 0.10m));
            values.Add(Close(opening.AddDays(d), 1000m + (0.10m * d)));
        }

        var contributions = ContributionsAndGrowth.Measure(1000m, 1003m, flows).Value!;
        var twr = TimeWeightedReturn.Measure(values, flows);

        // Oracle: hand. M2 0 / +3, all income (30 × 0.10); the daily ratios telescope to 1,003 / 1,000 - 1 = 0.30%.
        contributions.ContributionsUsd.Should().Be(0m);
        contributions.GrowthUsd.Should().Be(3m);
        contributions.IncomeUsd.Should().Be(3m);
        contributions.MarketUsd.Should().Be(0m);
        twr.Periods.Should().HaveCount(30);
        twr.Return.Value!.Value.Should().BeApproximately(0.003m, Exact);
    }

    // Fixture 17: wallet W holds 0.1 ETH (200) at the close of 2 Apr 2027 and receives a 100 TKX airdrop priced
    // at 0.50 on 3 Apr. The spam token is not counted at all (no flow, no value).
    [Fact]
    public void Fixture17_PricedAirdrop_IsIncome()
    {
        ClassifiedFlow[] flows = [Income(Day(2027, 4, 3), 50m)];
        var contributions = ContributionsAndGrowth.Measure(200m, 250m, flows).Value!;
        var twr = TimeWeightedReturn.Measure([Close(Day(2027, 4, 2), 200m), Close(Day(2027, 4, 3), 250m)], flows);

        // Oracle: hand. M2 0 / +50, all income; TWR 250 / 200 - 1 = 25%.
        contributions.ContributionsUsd.Should().Be(0m);
        contributions.IncomeUsd.Should().Be(50m);
        contributions.GrowthUsd.Should().Be(50m);
        twr.Return.Value.Should().Be(0.25m);
    }

    [Fact]
    public void Fixture17_UnpricedAirdrop_IsNotCounted()
    {
        var result = ContributionsAndGrowth.Measure(200m, 200m, []).Value!;

        // Oracle: hand. Nothing priced, nothing counted: growth 0 and no income.
        result.GrowthUsd.Should().Be(0m);
        result.IncomeUsd.Should().Be(0m);
    }

    // Fixture 18: 5 SOL (750) staked on Revolut X on 5 Apr 2027, a 0.02 SOL (3.00) reward on 20 Apr, 5.02 SOL
    // unstaked on 25 Apr.
    [Fact]
    public void Fixture18_StakingReward_IsIncomeStakingIsInternal()
    {
        var result = ContributionsAndGrowth.Measure(
            750m, 753m,
            [Internal(Day(2027, 4, 5), 750m), Income(Day(2027, 4, 20), 3m), Internal(Day(2027, 4, 25), 753m)]).Value!;

        // Oracle: hand. M2 0 / +3, all income.
        result.ContributionsUsd.Should().Be(0m);
        result.GrowthUsd.Should().Be(3m);
        result.IncomeUsd.Should().Be(3m);
    }

    // Fixture 19: EURUSD 1.10. Revolut X holds 1,000 at the close of 7 Apr 2027; €500 (550) arrives from the
    // linked Revolut bank on 8 Apr; on 9 Apr a €100 (110) card purchase on Binance from an unlinked card, of
    // which €2 (2.20) is the fee. A fictional 2,000 sits in the bank before the transfer.
    [Fact]
    public void Fixture19_CryptoSleeve_BothFiatLegsAreInflows()
    {
        ClassifiedFlow[] flows = [In(Day(2027, 4, 8), 550m), In(Day(2027, 4, 9), 110m), Fee(Day(2027, 4, 9), 2.20m)];
        var contributions = ContributionsAndGrowth.Measure(1000m, 1657.80m, flows).Value!;
        var twr = TimeWeightedReturn.Measure(
            [Close(Day(2027, 4, 7), 1000m), Close(Day(2027, 4, 8), 1550m), Close(Day(2027, 4, 9), 1657.80m)], flows);

        // Oracle: hand. Contributions 550 + 110 = 660, growth 657.80 - 660 = -2.20. 8 Apr: 1,550 / (1,000 + 550)
        // - 1 = 0; 9 Apr: 1,657.80 / (1,550 + 110) - 1 = -11/8300 = -0.1325%.
        contributions.ContributionsUsd.Should().Be(660m);
        contributions.GrowthUsd.Should().Be(-2.20m);
        twr.Periods[0].Return.Should().Be(0m);
        twr.Periods[1].Return.Should().BeApproximately(-11m / 8300m, Exact);
    }

    [Fact]
    public void Fixture19_Umbrella_TheBankLegIsInternalOnlyTheCardIsAnInflow()
    {
        ClassifiedFlow[] flows = [Internal(Day(2027, 4, 8), 550m), In(Day(2027, 4, 9), 110m), Fee(Day(2027, 4, 9), 2.20m)];

        // Bank 2,000 → 1,450 after the transfer; sleeve 1,000 → 1,657.80.
        var result = ContributionsAndGrowth.Measure(3000m, 3107.80m, flows).Value!;

        // Oracle: hand. 3,107.80 - 3,000 = 107.80 = 110 put in - 2.20 fee.
        result.ContributionsUsd.Should().Be(110m);
        result.GrowthUsd.Should().Be(-2.20m);
    }

    // Fixture 20: Revolut X sends 1 ETH (2,000) to linked wallet W, which receives 0.999 (1,998); Binance
    // receives 0.999 ETH (1,998) from a third party, all on 10 Apr 2027. Umbrella opening 2,000 at the close of
    // 9 Apr; 3,996 at the close of 10 Apr.
    [Fact]
    public void Fixture20_ResolvedByEnrichment_ContributionsAreTheThirdPartyDeposit()
    {
        var day = Day(2027, 4, 10);
        var result = ContributionsAndGrowth.Measure(
            2000m, 3996m, [Internal(day, 1998m), Fee(day, 2m), In(day, 1998m)]).Value!;

        // Oracle: hand. Contributions +1,998 (the Binance deposit), growth 1,996 - 1,998 = -2 (the network fee).
        result.ContributionsUsd.Should().Be(1998m);
        result.GrowthUsd.Should().Be(-2m);
    }

    [Fact]
    public void Fixture20_BeforeMatching_ProvisionalFlowsHideTheFee()
    {
        var day = Day(2027, 4, 10);
        var result = ContributionsAndGrowth.Measure(
            2000m, 3996m, [Out(day, 2000m), In(day, 1998m), In(day, 1998m)]).Value!;

        // Oracle: hand. 1,998 + 1,998 - 2,000 = 1,996 contributions, growth 0: the -2 fee disappears inside the
        // unmatched legs, which is why that stage is DEGRADED until the legs are matched.
        result.ContributionsUsd.Should().Be(1996m);
        result.GrowthUsd.Should().Be(0m);
    }
}
