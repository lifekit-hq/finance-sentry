namespace FinanceSentry.Modules.Research.Tests.Unit.Tax;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Positions;
using FinanceSentry.Modules.Research.Domain.Tax;
using FluentAssertions;
using Moq;
using Xunit;

public class DisposalTaxContextReaderTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly BookFigurePosition Position = new("VUAA", "etf", 10m, 1000m, 1200m, "ibkr", "Verified");

    private static PositionReturnsReader.Reading Reading(string coverage = PositionReturnCalculator.Complete) => new(
        new PositionLedger(
            [new LedgerFill("ibkr", new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc), 10m, 100m, null, null, "USD")],
            []),
        new PositionReturns(null, null, null, null, null, coverage, null, null, 1));

    private static DisposalTaxContextReader Reader(
        DisposalRegime? regime, Mock<IHistoricalExchangeRateService> rates)
    {
        var regimes = new Mock<IInstrumentTaxRegimeSource>();
        regimes.Setup(r => r.GetForSymbolAsync(User, "VUAA", It.IsAny<CancellationToken>())).ReturnsAsync(regime);
        return new DisposalTaxContextReader(regimes.Object, rates.Object);
    }

    [Fact]
    public async Task Read_AWithheldPosition_NeverFetchesFxSeries()
    {
        var rates = new Mock<IHistoricalExchangeRateService>();

        var context = await Reader(regime: null, rates).ReadAsync(User, Position, Reading(), CancellationToken.None);

        context.WithheldReasons.Should().Equal(DisposalTaxCalculator.Unclassified);
        rates.Verify(
            r => r.GetDailySeriesAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Read_AClearedPosition_ConvertsThroughTheEuroSeries()
    {
        var rates = new Mock<IHistoricalExchangeRateService>();
        rates
            .Setup(r => r.GetDailySeriesAsync("EUR", It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<DateOnly, decimal> { [new DateOnly(2024, 1, 10)] = 1.25m });

        var context = await Reader(DisposalRegime.Cgt, rates).ReadAsync(User, Position, Reading(), CancellationToken.None);

        context.Status.Should().Be(DisposalTaxCalculator.Shown);
        context.CostEur.Should().Be(800m); // 1,000 USD at 1.25 on the purchase date
        rates.Verify(
            r => r.GetDailySeriesAsync("EUR", new DateOnly(2024, 1, 10), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Read_PartialCoverage_IsWithheldWithoutFx()
    {
        var rates = new Mock<IHistoricalExchangeRateService>();

        var context = await Reader(DisposalRegime.Cgt, rates)
            .ReadAsync(User, Position, Reading(PositionReturnCalculator.Partial), CancellationToken.None);

        context.WithheldReasons.Should().Equal(DisposalTaxCalculator.PartialCoverage);
        rates.VerifyNoOtherCalls();
    }
}
