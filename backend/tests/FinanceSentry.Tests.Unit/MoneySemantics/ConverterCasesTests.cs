namespace FinanceSentry.Tests.Unit.MoneySemantics;

using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Tests.Unit.Fx;
using FluentAssertions;
using Xunit;

/// <summary>
/// Issue #1470, section A cases 1-6, 10 and 12: the conversion primitive and how a figure's FX status travels.
/// Expected values are written from the blessed table (docs/money-semantics.md section 3), never read off the code.
/// Seed rates: USD 1.00, EUR 1.08, GBP 1.27, UAH 0.024.
/// </summary>
public sealed class ConverterCasesTests
{
    [Fact]
    public void Case_01_ToUsd_Usd_IsItself_AndExact()
    {
        using var _ = FxTableScope.Seed();

        var result = CurrencyConverter.Convert(100m, "USD");

        result.Amount.Should().Be(100m);
        result.Status.Should().Be(FxStatus.Exact);
    }

    [Fact]
    public void Case_02_ToUsd_Eur_OnTheSeedTable_Is108_SeedTable()
    {
        using var _ = FxTableScope.Seed();

        var result = CurrencyConverter.Convert(100m, "EUR");

        result.Amount.Should().Be(108.00m);
        result.Status.Should().Be(FxStatus.SeedTable);
    }

    [Fact]
    public void Case_03_ToUsd_Uah_OnTheSeedTable_Is24_SeedTable()
    {
        using var _ = FxTableScope.Seed();

        var result = CurrencyConverter.Convert(1000m, "UAH");

        result.Amount.Should().Be(24.00m);
        result.Status.Should().Be(FxStatus.SeedTable);
    }

    [Fact]
    public void Case_04_ToUsd_ACurrencyNotInTheTable_PassesThrough_Unknown1To1_WithoutThrowing()
    {
        using var _ = FxTableScope.Seed();

        var act = () => CurrencyConverter.Convert(100m, "PLN");

        var result = act.Should().NotThrow().Subject;
        result.Amount.Should().Be(100m);
        result.Status.Should().Be(FxStatus.Unknown1To1);
    }

    [Fact]
    public void Case_05_AFigureSumming100Eur_And100Pln_CarriesTheWorstStatus_Unknown1To1()
    {
        using var _ = FxTableScope.Seed();

        var statuses = new[] { CurrencyConverter.Convert(100m, "EUR").Status, CurrencyConverter.Convert(100m, "PLN").Status };
        var worst = statuses.Aggregate(FxStatus.Exact, (a, b) => a.Worst(b));

        worst.Should().Be(FxStatus.Unknown1To1);
        worst.ToReason().Should().Be("FX_UNKNOWN_1TO1");
    }

    [Fact]
    public void Case_06_AFigureSumming100Eur_And100Usd_OnTheSeedTable_CarriesSeedTable_AndTheTodayRateReason()
    {
        using var _ = FxTableScope.Seed();

        var statuses = new[] { CurrencyConverter.Convert(100m, "EUR").Status, CurrencyConverter.Convert(100m, "USD").Status };
        var worst = statuses.Aggregate(FxStatus.Exact, (a, b) => a.Worst(b));

        worst.Should().Be(FxStatus.SeedTable);
        worst.ToReason().Should().Be("FX_TODAY_RATE");
    }

    [Fact]
    public void Case_10_FromUsd_108ToEur_Is100_AndAnUnrateableBtcStaysAtUsd108()
    {
        using var _ = FxTableScope.Seed();

        CurrencyConverter.FromUsd(108m, "EUR").Should().Be(100.00m);
        CurrencyConverter.FromUsd(108m, "BTC").Should().Be(108m);
    }

    [Fact]
    public async Task Case_12_DatedEurBoundary_WithNoPublishedRateForUah_IsRateMissing_AmountNull_NotZero_NoLiveFallback()
    {
        using var _ = FxTableScope.Seed();
        var date = new DateOnly(2026, 3, 6);
        var sut = new EurReportingConverter(new EurOnlyFeed(date, 1.10m));

        var result = await sut.ToEurAsync(100m, "UAH", date);

        result.RateMissing.Should().BeTrue();
        result.AmountEur.Should().BeNull();
    }

    /// <summary>A feed that published EUR for one date and nothing else (no UAH within any lookback).</summary>
    private sealed class EurOnlyFeed(DateOnly date, decimal usdPerEur) : IHistoricalExchangeRateService
    {
        public Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailySeriesAsync(
            string currency, DateOnly from, DateOnly to, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<DateOnly, decimal>> GetPublishedSeriesAsync(
            string currency, DateOnly from, DateOnly to, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<decimal?> GetPublishedRateAsync(string currency, DateOnly onDate, CancellationToken ct = default) =>
            Task.FromResult<decimal?>(currency == "EUR" && onDate == date ? usdPerEur : null);

        public Task<ConvertResult> ConvertToUsdAsync(
            decimal amount, string currency, DateOnly onDate, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
