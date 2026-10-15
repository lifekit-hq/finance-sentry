namespace FinanceSentry.Tests.Unit.Core.Measures;

using FinanceSentry.Core.Measures;
using FluentAssertions;
using Xunit;
using static FinanceSentry.Tests.Unit.Core.Measures.MeasureFixture;

/// <summary>
/// Golden fixtures 1-10 of the measure engine (finance-framework §3.4). Every expected value was computed
/// outside the engine, and the comment above it names how: "hand" is the closed form written out (kept as a
/// fraction where it is one), "spreadsheet" is LibreOffice Calc 7.4.7.2's XIRR on the same dated cash flows
/// (see <see cref="XirrTests"/> for why it is a fair oracle). Inflows sit at the start of their day and
/// outflows at the end (rule 7), so in XIRR an inflow is dated the close before its day.
/// </summary>
public class MeasureEngineGoldenFixtureTests
{
    private const double RateTolerance = 0.000001;

    // Fixture 1: the worked example of finance-framework §3.5. Opening 10,000 at the close of 31 Dec 2026;
    // markets +10% to 11,000 by the close of 30 Jun; a 10,000 deposit at the start of 1 Jul; markets to 19,800
    // by the close of 31 Dec 2027.
    private static readonly ValuePoint[] Fixture1Values =
    [
        Close(Day(2026, 12, 31), 10000m),
        Close(Day(2027, 6, 30), 11000m),
        Close(Day(2027, 12, 31), 19800m),
    ];

    private static readonly ClassifiedFlow[] Fixture1Flows = [In(Day(2027, 7, 1), 10000m)];

    [Fact]
    public void Fixture1_WorkedExample_ValueChangeIncludesTheDeposit()
    {
        var result = ValueChange.Measure(10000m, 19800m, scopeChanged: false);

        // Oracle: hand. 19,800 - 10,000 = 9,800, and 9,800 / 10,000 = 98%.
        result.ChangeUsd.Should().Be(9800m);
        result.Percent.Value.Should().Be(0.98m);
    }

    [Fact]
    public void Fixture1_WorkedExample_ContributionsTenThousandGrowthMinusTwoHundred()
    {
        var result = ContributionsAndGrowth.Measure(10000m, 19800m, Fixture1Flows).Value!;

        // Oracle: hand. Contributions = the one deposit; growth = 9,800 - 10,000.
        result.ContributionsUsd.Should().Be(10000m);
        result.GrowthUsd.Should().Be(-200m);
        result.MarketUsd.Should().Be(-200m);
    }

    [Fact]
    public void Fixture1_WorkedExample_TimeWeightedReturnIgnoresTheDepositTiming()
    {
        var result = TimeWeightedReturn.Measure(Fixture1Values, Fixture1Flows);

        // Oracle: hand. (11,000 / 10,000) × (19,800 / (11,000 + 10,000)) - 1 = 1.10 × 0.942857 - 1 = 13/350.
        result.Return.Value!.Value.Should().BeApproximately(13m / 350m, Exact);
        result.Periods.Select(p => p.Return).Should().Equal(0.1m, (19800m / 21000m) - 1m);
    }

    [Fact]
    public void Fixture1_WorkedExample_OneYearWindowIsXirrAnnualised()
    {
        var result = MoneyWeightedReturn.Measure(Fixture1Values[0], Fixture1Values[^1], Fixture1Flows);

        // Oracle: spreadsheet XIRR(-10,000 on 31 Dec 2026; -10,000 on 30 Jun 2027; +19,800 on 31 Dec 2027)
        // = -0.0132821454319989. The §3.5 idealisation (exponent exactly 0.5) gives -1.3318% by hand:
        // y^2 + y = 1.98, y = (-1 + √8.92) / 2; the 181/365 actual-day exponent moves it to -1.3282%.
        result.Method.Should().Be(MoneyWeightedMethod.Xirr);
        ((double)result.Return.Value!.Value).Should().BeApproximately(-0.0132821454319989, RateTolerance);
    }

    [Fact]
    public void Fixture1_WorkedExample_ModifiedDietzWeightsTheDepositByDaysInvested()
    {
        var result = MoneyWeightedReturn.ModifiedDietz(Fixture1Values[0], Fixture1Values[^1], Fixture1Flows);

        // Oracle: hand. The deposit is invested from the close of 30 Jun: 184 of 365 days, so
        // -200 / (10,000 + 184/365 × 10,000) = -73/5490 = -1.3297% (§3.5 rounds the weight to 0.5: -1.33%).
        result.Value!.Value.Should().BeApproximately(-73m / 5490m, Exact);
    }

    // Fixture 2: flows on the first and the last day of the window. Opening 1,000 at the close of
    // 31 Dec 2026; 500 deposited at the start of 1 Jan (close 1,530); 200 withdrawn at the end of 2 Jan
    // (close 1,345.30). A 50 deposit dated 31 Dec is already inside the opening value.
    private static readonly ValuePoint[] Fixture2Values =
    [
        Close(Day(2026, 12, 31), 1000m),
        Close(Day(2027, 1, 1), 1530m),
        Close(Day(2027, 1, 2), 1345.30m),
    ];

    private static readonly ClassifiedFlow[] Fixture2Flows =
        [In(Day(2026, 12, 31), 50m), In(Day(2027, 1, 1), 500m), Out(Day(2027, 1, 2), 200m)];

    [Fact]
    public void Fixture2_FlowsOnFirstAndLastDay_TimeWeightedReturn()
    {
        var result = TimeWeightedReturn.Measure(Fixture2Values, Fixture2Flows);

        // Oracle: hand. 1 Jan: 1,530 / (1,000 + 500) - 1 = 2%; 2 Jan: (1,345.30 + 200) / 1,530 - 1 = 1%;
        // 1.02 × 1.01 - 1 = 3.02%. The 31 Dec deposit is outside the window and moves nothing.
        result.Periods.Select(p => p.Return).Should().Equal(0.02m, 0.01m);
        result.Return.Value!.Value.Should().BeApproximately(0.0302m, Exact);
    }

    [Fact]
    public void Fixture2_FlowsOnFirstAndLastDay_ContributionsCountOnlyTheWindowsFlows()
    {
        var windowFlows = MeasureWindow.FlowsWithin(Fixture2Flows, Day(2026, 12, 31), Day(2027, 1, 2));
        var result = ContributionsAndGrowth.Measure(1000m, 1345.30m, windowFlows).Value!;

        // Oracle: hand. Contributions 500 - 200 = 300; growth 345.30 - 300 = 45.30.
        result.ContributionsUsd.Should().Be(300m);
        result.GrowthUsd.Should().Be(45.30m);
    }

    [Fact]
    public void Fixture2_FlowsOnFirstAndLastDay_ModifiedDietzWeightsOneAndZero()
    {
        var result = MoneyWeightedReturn.Measure(Fixture2Values[0], Fixture2Values[^1], Fixture2Flows);

        // Oracle: hand. The deposit is invested the whole window (weight 1), the withdrawal none of it
        // (weight 0): 45.30 / (1,000 + 500) = 3.02%, not annualised.
        result.Method.Should().Be(MoneyWeightedMethod.ModifiedDietz);
        result.Return.Value!.Value.Should().BeApproximately(0.0302m, Exact);
    }

    // Fixture 3: everything withdrawn to zero, an empty day, then a new deposit. Opening 1,000 at the close
    // of 31 Dec 2026; +10% to 1,100 by 5 Jan; 1,100 withdrawn at the end of 6 Jan; empty on 7 Jan; 500
    // deposited at the start of 8 Jan; +10% to 550 by 9 Jan.
    private static readonly ValuePoint[] Fixture3Values =
    [
        Close(Day(2026, 12, 31), 1000m),
        Close(Day(2027, 1, 5), 1100m),
        Close(Day(2027, 1, 6), 0m),
        Close(Day(2027, 1, 7), 0m),
        Close(Day(2027, 1, 8), 500m),
        Close(Day(2027, 1, 9), 550m),
    ];

    private static readonly ClassifiedFlow[] Fixture3Flows = [Out(Day(2027, 1, 6), 1100m), In(Day(2027, 1, 8), 500m)];

    [Fact]
    public void Fixture3_FullWithdrawalThenDeposit_ChainsOnlyTheInvestedStretches()
    {
        var result = TimeWeightedReturn.Measure(Fixture3Values, Fixture3Flows);

        // Oracle: hand. 5 Jan +10%; 6 Jan (0 + 1,100) / 1,100 - 1 = 0; 7 Jan empty to empty, no return;
        // 8 Jan 500 / (0 + 500) - 1 = 0; 9 Jan +10%. 1.1 × 1.1 - 1 = 21%.
        result.Return.Value!.Value.Should().BeApproximately(0.21m, Exact);
        result.Periods.Select(p => p.To).Should().NotContain(Day(2027, 1, 7));
        result.UnexplainedDays.Should().BeEmpty();
    }

    [Fact]
    public void Fixture3_ValueReappearingWithoutAnInflow_IsUnexplainedNotGrowth()
    {
        // The 8 Jan deposit is missing from the flows: value appears from an empty scope.
        var result = TimeWeightedReturn.Measure(Fixture3Values, [Out(Day(2027, 1, 6), 1100m)]);

        // Oracle: hand. The 8 Jan sub-period has a zero base and a non-zero end, so it cannot be a return:
        // it is left out (UNKNOWN_FLOW) and the chain keeps 1.1 × 1.0 × 1.1 - 1 = 21%.
        result.UnexplainedDays.Should().Equal(Day(2027, 1, 8));
        result.Return.Value!.Value.Should().BeApproximately(0.21m, Exact);
    }

    [Fact]
    public void Fixture3_FullWithdrawalThenDeposit_ModifiedDietz()
    {
        var result = MoneyWeightedReturn.Measure(Fixture3Values[0], Fixture3Values[^1], Fixture3Flows);

        // Oracle: hand. T = 9 days; the withdrawal leaves at the close of 6 Jan (weight 3/9), the deposit is
        // invested from the close of 7 Jan (weight 2/9). Growth = 550 - 1,000 - (500 - 1,100) = 150;
        // 150 / (1,000 - 1,100 × 3/9 + 500 × 2/9) = 27/134.
        result.Return.Value!.Value.Should().BeApproximately(27m / 134m, Exact);
    }

    [Fact]
    public void Fixture3_FullWithdrawalThenDepositOverAYear_Xirr()
    {
        // The same shape over a calendar year: 1,000 opening; 1,100 out at the end of 30 Jun 2026; 500 in
        // at the start of 1 Oct; 560 at the close of 31 Dec 2026.
        var result = MoneyWeightedReturn.Measure(
            Close(Day(2025, 12, 31), 1000m),
            Close(Day(2026, 12, 31), 560m),
            [Out(Day(2026, 6, 30), 1100m), In(Day(2026, 10, 1), 500m)]);

        // Oracle: spreadsheet XIRR(-1,000 on 31 Dec 2025; +1,100 on 30 Jun 2026; -500 on 30 Sep 2026;
        // +560 on 31 Dec 2026) = 0.269684639629158.
        result.Method.Should().Be(MoneyWeightedMethod.Xirr);
        ((double)result.Return.Value!.Value).Should().BeApproximately(0.269684639629158, RateTolerance);
    }

    // Fixture 4: a negative-value day (a margin deficit). 1,000 at the close of 31 Dec 2026; -100 on 1 Jan;
    // -50 on 2 Jan; 500 deposited at the start of 3 Jan, close 480.
    private static readonly ValuePoint[] Fixture4Values =
    [
        Close(Day(2026, 12, 31), 1000m),
        Close(Day(2027, 1, 1), -100m),
        Close(Day(2027, 1, 2), -50m),
        Close(Day(2027, 1, 3), 480m),
    ];

    private static readonly ClassifiedFlow[] Fixture4Flows = [In(Day(2027, 1, 3), 500m)];

    [Fact]
    public void Fixture4_NegativeValueDay_TimeWeightedReturnFailsNegativeBase()
    {
        var result = TimeWeightedReturn.Measure(Fixture4Values, Fixture4Flows);

        // Oracle: hand. The 2 Jan sub-period's base is -100: a percent of a deficit has no meaning, and
        // chaining through it would flip the sign of the whole product.
        result.Return.Failure.Should().Be(MeasureFailure.NegativeBase);
        result.Return.Value.Should().BeNull();
    }

    [Fact]
    public void Fixture4_NegativeValueDay_MoneyFiguresStillStand()
    {
        var change = ValueChange.Measure(1000m, 480m, scopeChanged: false);
        var contributions = ContributionsAndGrowth.Measure(1000m, 480m, Fixture4Flows).Value!;
        var dietz = MoneyWeightedReturn.Measure(Fixture4Values[0], Fixture4Values[^1], Fixture4Flows);

        // Oracle: hand. Change 480 - 1,000 = -520 (-52%); growth -520 - 500 = -1,020; Modified Dietz with the
        // deposit invested from the close of 2 Jan (weight 1/3): -1,020 / (1,000 + 500/3) = -153/175.
        change.ChangeUsd.Should().Be(-520m);
        change.Percent.Value.Should().Be(-0.52m);
        contributions.GrowthUsd.Should().Be(-1020m);
        dietz.Return.Value!.Value.Should().BeApproximately(-153m / 175m, Exact);
    }

    // Fixture 5: a missing-price day. 1,000 at the close of 31 Dec 2026; 1,010 on 1 Jan; no valuation on
    // 2 Jan; 100 deposited at the start of 3 Jan, close 1,131.
    private static readonly ValuePoint[] Fixture5Values =
    [
        Close(Day(2026, 12, 31), 1000m),
        Close(Day(2027, 1, 1), 1010m),
        Close(Day(2027, 1, 2), null),
        Close(Day(2027, 1, 3), 1131m),
    ];

    [Fact]
    public void Fixture5_MissingPriceDay_BridgedNotReadAsZero()
    {
        var result = TimeWeightedReturn.Measure(Fixture5Values, [In(Day(2027, 1, 3), 100m)]);

        // Oracle: hand. 1 Jan +1%; 2-3 Jan is one sub-period from the 1 Jan close: 1,131 / (1,010 + 100) - 1
        // = 7/370; 1.01 × 1,131 / 1,110 - 1 = 1077/37000 = 2.9108%. Reading 2 Jan as zero would instead
        // book a -100% day and leave 3 Jan on a base of only the deposit.
        result.MissingValueDays.Should().Equal(Day(2027, 1, 2));
        result.Periods.Select(p => p.Return).Should().Equal(0.01m, (1131m / 1110m) - 1m);
        result.Return.Value!.Value.Should().BeApproximately(1077m / 37000m, Exact);
    }

    [Fact]
    public void Fixture5_MissingOpeningValue_FailsMissingValue()
    {
        var result = TimeWeightedReturn.Measure([Close(Day(2026, 12, 31), null), .. Fixture5Values[1..]], []);

        result.Return.Failure.Should().Be(MeasureFailure.MissingValue);
    }

    // Fixture 6: a 2-for-1 stock split. 10 units bought for 1,000 on 4 Jan 2027; split 2-for-1 on 1 Feb;
    // 5 units sold for 300 on 1 Mar; 15 units held at 62.
    private static readonly CostEvent[] Fixture6Events =
    [
        new(Day(2027, 1, 4), CostEventKind.Buy, Quantity: 10m, AmountUsd: 1000m),
        new(Day(2027, 2, 1), CostEventKind.Split, Ratio: 2m),
        new(Day(2027, 3, 1), CostEventKind.Sell, Quantity: 5m, AmountUsd: 300m),
    ];

    [Fact]
    public void Fixture6_StockSplit_AverageCostHalvesAndGainsSplitIntoRealizedAndUnrealized()
    {
        var result = GainVsCost.Measure(Fixture6Events, 15m * 62m).Value!;

        // Oracle: hand. After the split 20 units cost 1,000 (50 each). Selling 5 realises 300 - 5 × 50 = 50;
        // 15 units cost 750 and are worth 930, so unrealized 180. Check: 300 + 930 - 1,000 = 230 = 50 + 180.
        result.QuantityHeld.Should().Be(15m);
        result.AverageCostUsd.Should().Be(50m);
        result.CostBasisUsd.Should().Be(750m);
        result.RealizedUsd.Should().Be(50m);
        result.UnrealizedUsd.Should().Be(180m);
        result.TotalGainUsd.Should().Be(230m);
    }

    [Fact]
    public void Fixture6_StockSplit_IsNeitherAFlowNorAReturn()
    {
        // 10 units at 120 before the split and 20 at 60 after it: no flow is recorded for a split.
        var result = TimeWeightedReturn.Measure([Close(Day(2027, 1, 29), 1200m), Close(Day(2027, 2, 1), 1200m)], []);

        // Oracle: hand. 1,200 / 1,200 - 1 = 0.
        result.Return.Value.Should().Be(0m);
    }

    [Fact]
    public void Fixture6_SplitMissingFromTheHistory_FailsUnexplainedQuantity()
    {
        // Without the split the history holds 10 units, so a sale of 15 cannot be explained.
        var result = GainVsCost.Measure(
            [Fixture6Events[0], new(Day(2027, 3, 1), CostEventKind.Sell, Quantity: 15m, AmountUsd: 900m)], 0m);

        result.Failure.Should().Be(MeasureFailure.UnexplainedQuantity);
    }

    // Fixture 7: an FX-only move. €1,000 held throughout; EURUSD 1.10 at the close of 31 Dec 2026 and 1.20 at
    // the close of 31 Jan 2027.
    [Fact]
    public void Fixture7_FxOnlyMove_ReturnIsAllTranslation()
    {
        var twr = TimeWeightedReturn.Measure([Close(Day(2026, 12, 31), 1100m), Close(Day(2027, 1, 31), 1200m)], []);
        var split = NetWorthBridge.Translate(1000m, 1000m, 1.10m, 1.20m);
        var bridge = NetWorthBridge.Measure(1100m, 1200m, [], new BridgePositionLines(
            split.LocalMoveUsd, split.TranslationUsd, 0m, 0m, 0m)).Value!;

        // Oracle: hand. USD value 1,000 × 1.10 = 1,100 → 1,000 × 1.20 = 1,200: TWR 1/11 = 9.0909%. Local move
        // (1,000 - 1,000) × 1.10 = 0; translation 1,000 × (1.20 - 1.10) = 100. Bridge: 100 = 0 savings + 0 market
        // + 100 FX, residual 0.
        twr.Return.Value!.Value.Should().BeApproximately(1m / 11m, Exact);
        split.Should().Be(new TranslationSplit(0m, 100m));
        bridge.SavingsUsd.Should().Be(0m);
        bridge.MarketUsd.Should().Be(0m);
        bridge.FxTranslationUsd.Should().Be(100m);
        bridge.ResidualUsd.Should().Be(0m);
        bridge.ResidualAboveTolerance.Should().BeFalse();
    }

    [Fact]
    public void Fixture7_FxOnlyMove_GainVsCostIsAllCurrencyEffect()
    {
        // EUR 1,000 bought at 1.10 (USD 1,100), still held, worth EUR 1,000 at 1.20 (USD 1,200).
        var result = GainVsCost.Measure(
            [new(Day(2026, 12, 31), CostEventKind.Buy, Quantity: 1000m, AmountUsd: 1100m, AmountLocal: 1000m)],
            1200m,
            new LocalValue(1000m, 1.20m)).Value!;

        // Oracle: hand. USD gain 1,200 - 1,100 = 100. Price effect = local gain (1,000 - 1,000) × 1.20 = 0;
        // currency effect = 100 - 0 = 100 (= 1,000 × (1.20 - 1.10)). Nothing realized.
        result.CurrencyBasis.Should().Be(GainCurrencyBasis.LocalAmounts);
        result.UnrealizedUsd.Should().Be(100m);
        result.UnrealizedCurrencyUsd.Should().Be(100m);
        result.PriceEffectUsd.Should().Be(0m);
        result.CurrencyEffectUsd.Should().Be(100m);
        result.RealizedUsd.Should().Be(0m);
        result.TotalGainUsd.Should().Be(100m);
    }

    [Fact]
    public void Fixture7_PriceAndFxMovesTogether_GainsSplitIntoPriceAndCurrencyEffect()
    {
        // 10 units bought for EUR 1,000 at 1.10 (USD 1,100); 5 sold for EUR 600 at 1.30 (USD 780);
        // the 5 left are worth EUR 520 at 1.25 (USD 650).
        var result = GainVsCost.Measure(
            [
                new(Day(2027, 1, 4), CostEventKind.Buy, Quantity: 10m, AmountUsd: 1100m, AmountLocal: 1000m),
                new(Day(2027, 2, 1), CostEventKind.Sell, Quantity: 5m, AmountUsd: 780m, AmountLocal: 600m, RateUsd: 1.30m),
            ],
            650m,
            new LocalValue(520m, 1.25m)).Value!;

        // Oracle: hand. Sold half: cost EUR 500 = USD 550. Realized USD 780 - 550 = 230; price effect
        // (600 - 500) × 1.30 = 130; currency effect 230 - 130 = 100 (= 500 × (1.30 - 1.10)).
        // Held half: cost EUR 500 = USD 550, value USD 650, unrealized USD 100; price effect (520 - 500) × 1.25
        // = 25; currency effect 100 - 25 = 75 (= 500 × (1.25 - 1.10)).
        // Totals: USD gain 330 = price 155 + currency 175.
        result.RealizedUsd.Should().Be(230m);
        result.RealizedCurrencyUsd.Should().Be(100m);
        result.UnrealizedUsd.Should().Be(100m);
        result.UnrealizedCurrencyUsd.Should().Be(75m);
        result.PriceEffectUsd.Should().Be(155m);
        result.CurrencyEffectUsd.Should().Be(175m);
        result.TotalGainUsd.Should().Be(330m);
    }

    [Fact]
    public void Fixture7_FxLineLeftOut_ResidualAboveTolerance()
    {
        var bridge = NetWorthBridge.Measure(1100m, 1200m, [], new BridgePositionLines(0m, 0m, 0m, 0m, 0m)).Value!;

        // Oracle: hand. The unexplained 100 is 9.1% of the 1,100 start, above the 0.2% tolerance (2.20).
        bridge.ResidualUsd.Should().Be(100m);
        bridge.ResidualAboveTolerance.Should().BeTrue();
    }

    [Fact]
    public void Fixture7_ResidualAtTheTolerance_IsNotDegraded()
    {
        var bridge = NetWorthBridge.Measure(1100m, 1200m, [], new BridgePositionLines(0m, 97.80m, 0m, 0m, 0m)).Value!;

        // Oracle: hand. Residual 100 - 97.80 = 2.20 = 0.2% of 1,100: at the tolerance, not above it.
        bridge.ResidualUsd.Should().Be(2.20m);
        bridge.ResidualAboveTolerance.Should().BeFalse();
    }

    // Fixture 8: a sub-one-year window. 2,000 at the close of 31 Mar 2027; 1,000 deposited at the start of
    // 15 May; 3,150 at the close of 30 Jun.
    [Fact]
    public void Fixture8_SubOneYearWindow_ModifiedDietzNotAnnualised()
    {
        var result = MoneyWeightedReturn.Measure(
            Close(Day(2027, 3, 31), 2000m), Close(Day(2027, 6, 30), 3150m), [In(Day(2027, 5, 15), 1000m)]);

        // Oracle: hand. T = 91 days; the deposit is invested from the close of 14 May, 47 of them.
        // Growth 3,150 - 2,000 - 1,000 = 150; 150 / (2,000 + 47/91 × 1,000) = 273/4580 = 5.9607% for the
        // quarter. Annualising it would read about 26%; GIPS 2.A.12 forbids that.
        result.Method.Should().Be(MoneyWeightedMethod.ModifiedDietz);
        result.Return.Value!.Value.Should().BeApproximately(273m / 4580m, Exact);
    }

    [Theory]
    [InlineData(2028, 3, 30, MoneyWeightedMethod.ModifiedDietz)]
    [InlineData(2028, 3, 31, MoneyWeightedMethod.Xirr)]
    public void Fixture8_OneYearBoundary_IsTheCalendarYear(int year, int month, int day, MoneyWeightedMethod expected)
    {
        var result = MoneyWeightedReturn.Measure(Close(Day(2027, 3, 31), 2000m), Close(Day(year, month, day), 2100m), []);

        result.Method.Should().Be(expected);
    }

    // Fixture 9: an XIRR with no sign change. 1,000 at the close of 1 Jan 2026, worth nothing at the close of
    // 1 Jan 2027, nothing withdrawn.
    [Fact]
    public void Fixture9_XirrNoSignChange_TypedFailureNotZero()
    {
        var result = MoneyWeightedReturn.Measure(Close(Day(2026, 1, 1), 1000m), Close(Day(2027, 1, 1), 0m), []);

        // Oracle: spreadsheet XIRR(-1,000; 0) = Err:502. As in Excel and the ported library, a zero cash
        // flow counts on the paying-in side, so nothing was ever taken out.
        result.Method.Should().Be(MoneyWeightedMethod.Xirr);
        result.Return.Failure.Should().Be(MeasureFailure.NoSignChange);
        result.Return.Value.Should().BeNull();
    }

    [Fact]
    public void Fixture9_TwoYearWindowWithBothFlows_Xirr()
    {
        // The passing counterpart: 5,000 opening; 1,000 out at the end of 15 Sep 2026; 2,000 in at the start
        // of 1 Mar 2027; 7,300 at the close of 31 Dec 2027.
        var result = MoneyWeightedReturn.Measure(
            Close(Day(2025, 12, 31), 5000m),
            Close(Day(2027, 12, 31), 7300m),
            [Out(Day(2026, 9, 15), 1000m), In(Day(2027, 3, 1), 2000m)]);

        // Oracle: spreadsheet XIRR(-5,000 on 31 Dec 2025; +1,000 on 15 Sep 2026; -2,000 on 28 Feb 2027;
        // +7,300 on 31 Dec 2027) = 0.118826033725328.
        ((double)result.Return.Value!.Value).Should().BeApproximately(0.118826033725328, RateTolerance);
    }

    // Fixture 10: a zero starting value. A new account opened empty at the close of 31 Dec 2026; 2,000
    // deposited at the start of 1 Jan; 2,000 at the close of 2 Jan.
    [Fact]
    public void Fixture10_ZeroStartingValue_PercentsFailZeroBaseMoneyFiguresStand()
    {
        var opening = Close(Day(2026, 12, 31), 0m);
        var closing = Close(Day(2027, 1, 2), 2000m);
        ClassifiedFlow[] flows = [In(Day(2027, 1, 1), 2000m)];

        var change = ValueChange.Measure(0m, 2000m, scopeChanged: false);
        var contributions = ContributionsAndGrowth.Measure(0m, 2000m, flows).Value!;
        var twr = TimeWeightedReturn.Measure([opening, Close(Day(2027, 1, 1), 2000m), closing], flows);
        var mwr = MoneyWeightedReturn.Measure(opening, closing, flows);
        var xirr = MoneyWeightedReturn.Measure(opening, Close(Day(2027, 12, 31), 2000m), flows);

        // Oracle: hand. Change +2,000, all of it put in: contributions 2,000, growth 0. A percent of a zero
        // start does not exist (finance-framework §3.4, "Return base").
        change.ChangeUsd.Should().Be(2000m);
        change.Percent.Failure.Should().Be(MeasureFailure.ZeroBase);
        contributions.ContributionsUsd.Should().Be(2000m);
        contributions.GrowthUsd.Should().Be(0m);
        twr.Return.Failure.Should().Be(MeasureFailure.ZeroBase);
        mwr.Return.Failure.Should().Be(MeasureFailure.ZeroBase);
        xirr.Return.Failure.Should().Be(MeasureFailure.ZeroBase);
    }
}
