namespace FinanceSentry.Modules.Research.Tests.Unit.Tax;

using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Positions;
using FinanceSentry.Modules.Research.Domain.Tax;
using FluentAssertions;
using Xunit;

public class DisposalTaxCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    // USD per euro: 1.25 before 2025, 1.00 from 2025 on, 1.20 today. Any other currency is parity.
    private static decimal RateOn(string currency, DateOnly day) => currency switch
    {
        "EUR" => day.Year < 2025 ? 1.25m : 1.00m,
        _ => 1m,
    };

    private static decimal RateNow(string currency) => currency == "EUR" ? 1.20m : 1m;

    private static LedgerFill Fill(DateTime at, decimal quantity, decimal price, decimal? commission = null) =>
        new("ibkr", at, quantity, price, commission, commission is null ? null : "USD", "USD");

    private static DisposalTaxInput Input(
        IReadOnlyList<LedgerFill> fills,
        decimal held,
        decimal valueUsd,
        DisposalRegime? regime = DisposalRegime.Cgt,
        string provider = "ibkr",
        string coverage = PositionReturnCalculator.Complete,
        string? basis = "Verified") =>
        new("VUAA", provider, held, valueUsd, coverage, basis, regime, fills);

    private static DisposalTaxContext Run(DisposalTaxInput input, DateOnly? today = null) =>
        DisposalTaxCalculator.Compute(input, today ?? Today, RateOn, RateNow);

    [Fact]
    public void Cgt_WalksLotsFirstInFirstOut_AndConvertsEachCostOnItsOwnDate()
    {
        var context = Run(Input(
            [
                Fill(new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc), 10m, 100m),
                Fill(new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc), 10m, 150m),
                Fill(new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc), -5m, 160m),
            ],
            held: 15m,
            valueUsd: 3000m));

        context.Status.Should().Be(DisposalTaxCalculator.Shown);
        context.Regime.Should().Be("cgt");
        context.Currency.Should().Be("EUR");
        context.Lots.Should().HaveCount(2);

        // The sell took the oldest lot's first 5 units: 5 left at 100 USD / 1.25 = 400 EUR.
        context.Lots[0].Quantity.Should().Be(5m);
        context.Lots[0].CostEur.Should().Be(400m);
        context.Lots[1].CostEur.Should().Be(1500m);

        // Proceeds: 3000 USD / 1.20 = 2500 EUR, split by quantity.
        context.ProceedsEur.Should().Be(2500m);
        context.CostEur.Should().Be(1900m);
        context.GainEur.Should().Be(600m);
        context.Rate.Should().Be(0.33m);
        context.EstimatedTaxEur.Should().Be(198m);

        // 600 is inside the 1,270 annual exemption.
        context.AnnualExemptionEur.Should().Be(1270m);
        context.TaxAfterExemptionEur.Should().Be(0m);
        context.NextDeemedDisposalOn.Should().BeNull();
        context.TaxYear.Should().Be(2026);
        context.RatesCarriedForward.Should().BeFalse();
    }

    [Fact]
    public void Cgt_TaxAfterExemption_TaxesOnlyTheGainAboveIt()
    {
        var context = Run(Input(
            [Fill(new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc), 10m, 100m)],
            held: 10m,
            valueUsd: 6000m)); // 5,000 EUR proceeds against 800 EUR cost

        context.GainEur.Should().Be(4200m);
        context.EstimatedTaxEur.Should().Be(1386m);
        context.TaxAfterExemptionEur.Should().Be(966.9m); // (4,200 - 1,270) * 0.33
    }

    [Fact]
    public void Cgt_ALoss_OwesNothing()
    {
        var context = Run(Input(
            [Fill(new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc), 10m, 100m)],
            held: 10m,
            valueUsd: 600m));

        context.GainEur.Should().Be(-300m);
        context.EstimatedTaxEur.Should().Be(0m);
        context.TaxAfterExemptionEur.Should().Be(0m);
    }

    [Fact]
    public void Cgt_BuyingCommission_IsPartOfTheCost()
    {
        var context = Run(Input(
            [Fill(new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc), 10m, 100m, commission: -5m)],
            held: 10m,
            valueUsd: 1200m));

        context.CostEur.Should().Be(804m); // (1,000 + 5) / 1.25
    }

    [Fact]
    public void ExitTax_TaxesEachLotGain_AndNamesTheDeemedDisposalDates()
    {
        var context = Run(Input(
            [
                Fill(new DateTime(2023, 6, 1, 0, 0, 0, DateTimeKind.Utc), 10m, 100m),
                Fill(new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), 10m, 200m),
            ],
            held: 20m,
            valueUsd: 2400m,
            regime: DisposalRegime.ExitTax));

        context.Regime.Should().Be("exitTax");
        context.Rate.Should().Be(0.38m);
        context.AnnualExemptionEur.Should().BeNull();
        context.TaxAfterExemptionEur.Should().BeNull();
        context.DeemedDisposalYears.Should().Be(8);

        // Lot 1: 800 EUR cost, 1,000 EUR proceeds, +200. Lot 2: 2,000 EUR cost, 1,000 EUR proceeds, -1,000.
        context.Lots.Select(l => l.GainEur).Should().Equal(200m, -1000m);
        context.GainEur.Should().Be(-800m);

        // Per lot, with no loss relief across lots: only lot 1's gain is taxed.
        context.EstimatedTaxEur.Should().Be(76m);
        context.Lots.Select(l => l.DeemedDisposalOn).Should().Equal(new DateOnly(2031, 6, 1), new DateOnly(2033, 2, 1));
        context.NextDeemedDisposalOn.Should().Be(new DateOnly(2031, 6, 1));
    }

    [Fact]
    public void ExitTax_ALotPastItsEighthAnniversary_IsWithheld()
    {
        var context = Run(Input(
            [Fill(new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10m, 100m)],
            held: 10m,
            valueUsd: 2000m,
            regime: DisposalRegime.ExitTax));

        context.Status.Should().Be(DisposalTaxCalculator.Withheld);
        context.WithheldReasons.Should().Equal(DisposalTaxCalculator.DeemedDisposalPassed);
        context.EstimatedTaxEur.Should().BeNull();
        context.Lots.Should().BeEmpty();
    }

    [Fact]
    public void Cgt_ABuyAndASellWithinFourWeeks_IsWithheld()
    {
        var fills = new[]
        {
            Fill(new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc), 10m, 100m),
            Fill(new DateTime(2025, 3, 20, 0, 0, 0, DateTimeKind.Utc), -2m, 110m),
        };

        Run(Input(fills, held: 8m, valueUsd: 1000m)).WithheldReasons
            .Should().Equal(DisposalTaxCalculator.FourWeekRule);
    }

    [Fact]
    public void Cgt_ASellFarFromAnyBuy_IsNotWithheld()
    {
        var fills = new[]
        {
            Fill(new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc), 10m, 100m),
            Fill(new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc), -2m, 110m), // 29 days later
        };

        Run(Input(fills, held: 8m, valueUsd: 1000m)).Status.Should().Be(DisposalTaxCalculator.Shown);
    }

    [Fact]
    public void ExitTax_IgnoresTheFourWeekRule()
    {
        var fills = new[]
        {
            Fill(new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc), 10m, 100m),
            Fill(new DateTime(2025, 3, 20, 0, 0, 0, DateTimeKind.Utc), -2m, 110m),
        };

        Run(Input(fills, held: 8m, valueUsd: 1000m, regime: DisposalRegime.ExitTax)).Status
            .Should().Be(DisposalTaxCalculator.Shown);
    }

    [Fact]
    public void ANonBrokerPosition_IsWithheldAsUnsupported_AndNothingElseIsReported()
    {
        var context = Run(Input(
            [Fill(new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc), 10m, 100m)],
            held: 10m,
            valueUsd: 1200m,
            regime: null,
            provider: "binance",
            coverage: PositionReturnCalculator.Partial,
            basis: null));

        context.WithheldReasons.Should().Equal(DisposalTaxCalculator.ProviderNotSupported);
    }

    [Fact]
    public void EveryFailedGate_IsNamed()
    {
        var context = Run(Input(
            [Fill(new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc), 10m, 100m)],
            held: 10m,
            valueUsd: 1200m,
            regime: null,
            coverage: PositionReturnCalculator.Partial,
            basis: "Unverified"));

        context.Status.Should().Be(DisposalTaxCalculator.Withheld);
        context.Regime.Should().BeNull();
        context.Disclaimer.Should().Contain("not tax advice");
        context.WithheldReasons.Should().Equal(
            DisposalTaxCalculator.PartialCoverage,
            DisposalTaxCalculator.Unclassified,
            DisposalTaxCalculator.BasisNotVerified);
    }

    [Fact]
    public void AnUnknownBasisState_IsNotVerified()
    {
        var context = Run(Input(
            [Fill(new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc), 10m, 100m)],
            held: 10m,
            valueUsd: 1200m,
            basis: null));

        context.WithheldReasons.Should().Equal(DisposalTaxCalculator.BasisNotVerified);
    }

    [Fact]
    public void FillsThatDoNotAddUpToTheHeldQuantity_AreWithheldAsPartial()
    {
        var context = Run(Input(
            [Fill(new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc), 10m, 100m)],
            held: 12m,
            valueUsd: 1200m));

        context.WithheldReasons.Should().Equal(DisposalTaxCalculator.PartialCoverage);
    }

    [Fact]
    public void AYearWithoutItsOwnRates_ReadsTheLatestEarlierYear_AndSaysSo()
    {
        var context = Run(
            Input([Fill(new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc), 10m, 100m)], held: 10m, valueUsd: 1200m),
            today: new DateOnly(2027, 2, 1));

        context.TaxYear.Should().Be(2026);
        context.RatesCarriedForward.Should().BeTrue();
    }

    [Fact]
    public void ForYear_ReturnsTheExactRowWhenThereIsOne()
    {
        var rates = IrishTaxReference.ForYear(2026);

        rates.CarriedForward.Should().BeFalse();
        rates.Row.CgtRate.Should().Be(0.33m);
        rates.Row.CgtAnnualExemptionEur.Should().Be(1270m);
        rates.Row.ExitTaxRate.Should().Be(0.38m);
        rates.Row.DeemedDisposalYears.Should().Be(8);
        rates.Row.Source.Should().Contain("revenue.ie");
    }
}
