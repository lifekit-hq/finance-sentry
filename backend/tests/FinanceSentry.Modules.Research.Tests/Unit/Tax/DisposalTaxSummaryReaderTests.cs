namespace FinanceSentry.Modules.Research.Tests.Unit.Tax;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Tax;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>#454 S7: the disposal-tax summary counts the same per-position context the dossier shows.</summary>
public class DisposalTaxSummaryReaderTests
{
    private static readonly Guid User = Guid.NewGuid();

    private readonly Mock<IBookFiguresService> _book = new();
    private readonly Mock<IPositionLedgerSource> _ledger = new();
    private readonly Mock<IHistoricalExchangeRateService> _rates = new();
    private readonly Mock<IInstrumentTaxRegimeSource> _regimes = new();

    private DisposalTaxSummaryReader Reader()
    {
        var returns = new PositionReturnsReader(_ledger.Object, _rates.Object);
        return new DisposalTaxSummaryReader(_book.Object, returns, new DisposalTaxContextReader(_regimes.Object, _rates.Object));
    }

    private void Positions(params BookFigurePosition[] positions) => _book
        .Setup(b => b.ReadAsync(User, It.IsAny<CancellationToken>()))
        .ReturnsAsync(BookFigures.Empty with { Positions = positions });

    private void Bought(string symbol, decimal quantity) => _ledger
        .Setup(l => l.ReadForSymbolAsync(User, symbol, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PositionLedger(
            [new LedgerFill("ibkr", new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc), quantity, 100m, null, null, "USD")],
            []));

    private void Regime(string symbol, DisposalRegime? regime) => _regimes
        .Setup(r => r.GetForSymbolAsync(User, symbol, It.IsAny<CancellationToken>())).ReturnsAsync(regime);

    [Fact]
    public async Task NoPositions_IsAnEmptySummary_WithNothingEstimated()
    {
        Positions();

        var summary = await Reader().ReadAsync(User);

        summary.PositionCount.Should().Be(0);
        summary.EstimatedTaxEur.Should().BeNull();
        summary.WithheldReasons.Should().BeEmpty();
    }

    [Fact]
    public async Task AVenueHolding_IsCountedAsProviderNotSupported_WithoutReadingItsFills()
    {
        Positions(new BookFigurePosition("BTC", "crypto", 0.5m, null, 30_000m, "binance"));

        var summary = await Reader().ReadAsync(User);

        summary.PositionCount.Should().Be(1);
        summary.WithheldCount.Should().Be(1);
        summary.WithheldReasons.Should().Equal(new Dictionary<string, int> { [DisposalTaxCalculator.ProviderNotSupported] = 1 });
        _ledger.Verify(
            l => l.ReadForSymbolAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnUnclassifiedBrokerPosition_IsWithheldWithItsReason()
    {
        Positions(new BookFigurePosition("VUAA", "etf", 10m, 1000m, 1200m, "ibkr", "Verified"));
        Bought("VUAA", 10m);
        Regime("VUAA", null);

        var summary = await Reader().ReadAsync(User);

        summary.WithheldCount.Should().Be(1);
        summary.WithheldReasons.Should().ContainKey(DisposalTaxCalculator.Unclassified).WhoseValue.Should().Be(1);
        summary.EstimatedTaxEur.Should().BeNull();
    }

    [Fact]
    public async Task AClassifiedVerifiedPosition_IsShown_AndItsEstimateIsAdded()
    {
        Positions(
            new BookFigurePosition("VUAA", "etf", 10m, 1000m, 1200m, "ibkr", "Verified"),
            new BookFigurePosition("BTC", "crypto", 0.5m, null, 30_000m, "binance"));
        Bought("VUAA", 10m);
        Regime("VUAA", DisposalRegime.ExitTax);
        _rates
            .Setup(r => r.GetDailySeriesAsync("EUR", It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<DateOnly, decimal> { [new DateOnly(2024, 1, 10)] = 1.25m });

        var summary = await Reader().ReadAsync(User);

        summary.PositionCount.Should().Be(2);
        summary.ShownCount.Should().Be(1);
        summary.WithheldCount.Should().Be(1);
        summary.EstimatedTaxEur.Should().BeGreaterThan(0m);
        summary.TaxYear.Should().NotBeNull();
        summary.WithheldReasons.Should().ContainKey(DisposalTaxCalculator.ProviderNotSupported);
    }
}
