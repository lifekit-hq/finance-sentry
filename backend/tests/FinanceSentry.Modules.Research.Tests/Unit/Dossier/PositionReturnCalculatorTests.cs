namespace FinanceSentry.Modules.Research.Tests.Unit.Dossier;

using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Positions;
using FluentAssertions;
using Xunit;

public class PositionReturnCalculatorTests
{
    private static readonly DateTime Day = new(2026, 1, 5, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NoFillsOrIncome_IsUnavailableWithTheHoldingsOwnGain()
    {
        var returns = Compute(PositionLedger.Empty, held: 10m, valueUsd: 1500m, costUsd: 1200m);

        returns.Coverage.Should().Be(PositionReturnCalculator.Unavailable);
        returns.UnrealizedUsd.Should().Be(300m);
        returns.RealizedUsd.Should().BeNull();
        returns.TotalUsd.Should().Be(300m);
        returns.FillCount.Should().Be(0);
    }

    [Fact]
    public void NoFillsAndNoCostBasis_HasNoTotal()
    {
        var returns = Compute(PositionLedger.Empty, held: 10m, valueUsd: 1500m, costUsd: null);

        returns.UnrealizedUsd.Should().BeNull();
        returns.TotalUsd.Should().BeNull();
    }

    [Fact]
    public void BuysAndASell_SplitRealizedFromUnrealizedAtAverageCost()
    {
        var ledger = new PositionLedger(
            [
                Fill(0, 10m, 100m, commission: -1m),
                Fill(10, 10m, 120m),
                Fill(20, -5m, 150m, commission: -1m),
            ],
            [new LedgerIncome("ibkr", Day.AddDays(30), 12m, "USD"), new LedgerIncome("ibkr", Day.AddDays(30), -2m, "USD")]);

        var returns = Compute(ledger, held: 15m, valueUsd: 2400m, costUsd: null);

        // Cost 1000 + 1 commission + 1200 = 2201 for 20 units; the sell releases a quarter (550.25).
        returns.Coverage.Should().Be(PositionReturnCalculator.Complete);
        returns.RealizedUsd.Should().Be(198.75m, "5 x 150 - 1 commission - 550.25 released cost");
        returns.UnrealizedUsd.Should().Be(749.25m, "2400 now - 1650.75 open cost");
        returns.DividendsUsd.Should().Be(10m, "credit net of tax withheld");
        returns.CurrencyUsd.Should().Be(0m);
        returns.TotalUsd.Should().Be(958m);
        returns.AverageCostNative.Should().Be(110m);
        returns.CostCurrency.Should().Be("USD");
        returns.FillCount.Should().Be(3);
    }

    [Fact]
    public void NonUsdFills_SplitTheCurrencyMoveFromThePriceGain()
    {
        var ledger = new PositionLedger([Fill(0, 10m, 100m, currency: "EUR")], []);

        // Bought at 1.10 USD/EUR, the euro is 1.20 now; the holding is worth 1300 USD.
        var returns = PositionReturnCalculator.Compute(
            ledger, 10m, 1300m, null, (_, _) => 1.10m, _ => 1.20m);

        returns.CurrencyUsd.Should().Be(100m, "1000 EUR of cost moved from 1100 to 1200 USD");
        returns.UnrealizedUsd.Should().Be(100m, "1300 now - 1200 cost at today's rate");
        returns.AverageCostNative.Should().Be(100m);
        returns.CostCurrency.Should().Be("EUR");
    }

    [Fact]
    public void FillsThatDoNotExplainTheHolding_ArePartial()
    {
        var ledger = new PositionLedger([Fill(0, 5m, 100m)], [new LedgerIncome("ibkr", Day, 4m, "USD")]);

        var returns = Compute(ledger, held: 10m, valueUsd: 1500m, costUsd: 1000m);

        returns.Coverage.Should().Be(PositionReturnCalculator.Partial);
        returns.UnrealizedUsd.Should().Be(500m, "the holding's own cost-basis gain");
        returns.RealizedUsd.Should().BeNull();
        returns.CurrencyUsd.Should().BeNull();
        returns.DividendsUsd.Should().Be(4m);
        returns.AverageCostNative.Should().BeNull();
    }

    [Fact]
    public void SellBeforeAnyBuy_IsPartial()
    {
        var ledger = new PositionLedger([Fill(0, -5m, 100m), Fill(1, 5m, 90m)], []);

        Compute(ledger, held: 5m, valueUsd: 500m, costUsd: 450m).Coverage.Should().Be(PositionReturnCalculator.Partial);
    }

    [Fact]
    public void FullyClosedPosition_HasNoOpenCostOrAverage()
    {
        var ledger = new PositionLedger([Fill(0, 10m, 100m), Fill(5, -10m, 130m)], []);

        var returns = Compute(ledger, held: 0m, valueUsd: 0m, costUsd: null);

        returns.Coverage.Should().Be(PositionReturnCalculator.Complete);
        returns.RealizedUsd.Should().Be(300m);
        returns.UnrealizedUsd.Should().Be(0m);
        returns.AverageCostNative.Should().BeNull();
    }

    [Fact]
    public void GiftWithNoPrice_ReleasesCostWithoutRealizingAGain()
    {
        var ledger = new PositionLedger([Fill(0, 10m, 100m), Fill(5, -4m, 0m)], []);

        var returns = Compute(ledger, held: 6m, valueUsd: 700m, costUsd: null);

        returns.RealizedUsd.Should().Be(0m);
        returns.UnrealizedUsd.Should().Be(100m, "700 - 600 open cost");
    }

    [Fact]
    public void FillsInTwoCurrencies_HaveNoSingleAverageCost()
    {
        var ledger = new PositionLedger([Fill(0, 5m, 100m), Fill(1, 5m, 90m, currency: "EUR")], []);

        var returns = Compute(ledger, held: 10m, valueUsd: 1000m, costUsd: null);

        returns.Coverage.Should().Be(PositionReturnCalculator.Complete);
        returns.AverageCostNative.Should().BeNull();
        returns.CostCurrency.Should().BeNull();
    }

    private static PositionReturns Compute(PositionLedger ledger, decimal held, decimal valueUsd, decimal? costUsd)
        => PositionReturnCalculator.Compute(ledger, held, valueUsd, costUsd, (_, _) => 1m, _ => 1m);

    private static LedgerFill Fill(int day, decimal quantity, decimal price, decimal? commission = null, string currency = "USD")
        => new("ibkr", Day.AddDays(day), quantity, price, commission, null, currency);
}
