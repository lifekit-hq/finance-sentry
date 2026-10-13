namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetAssetDossierCryptoTests
{
    private const string Symbol = "BTC";

    private static readonly AnalystActionsResult Analysts = new(
        [], "inUniverse", DateTimeOffset.UtcNow, []);

    [Fact]
    public async Task CryptoHolding_DropsTheValuationAndTheAnalystsEvenWhenTheSourcesAnswer()
    {
        var analystHandler = new Mock<IQueryHandler<GetAnalystActionsQuery, AnalystActionsResult>>();
        analystHandler
            .Setup(h => h.Handle(It.IsAny<GetAnalystActionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Analysts);

        var dossier = await HandleAsync(
            Held(AssetClassNormalizer.Crypto), Valuation(notApplicable: false), analystHandler);

        dossier.Valuation.Should().BeNull();
        dossier.Analysts.Should().BeNull();
        analystHandler.Verify(
            h => h.Handle(It.IsAny<GetAnalystActionsQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnheldNotApplicableSymbol_KeepsOnlyTheNotApplicableMarker()
    {
        var analystHandler = new Mock<IQueryHandler<GetAnalystActionsQuery, AnalystActionsResult>>();
        analystHandler
            .Setup(h => h.Handle(It.IsAny<GetAnalystActionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Analysts);

        var dossier = await HandleAsync(null, ValuationSnapshotResult.ForNonEquity(Symbol), analystHandler);

        dossier.Valuation.Should().NotBeNull();
        dossier.Valuation!.NotApplicable.Should().BeTrue();
        dossier.Analysts.Should().BeNull();
    }

    [Fact]
    public async Task EquityHolding_KeepsTheValuationAndTheAnalysts()
    {
        var analystHandler = new Mock<IQueryHandler<GetAnalystActionsQuery, AnalystActionsResult>>();
        analystHandler
            .Setup(h => h.Handle(It.IsAny<GetAnalystActionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Analysts);

        var dossier = await HandleAsync(Held("Equities"), Valuation(notApplicable: false), analystHandler);

        dossier.Valuation.Should().NotBeNull();
        dossier.Valuation!.NotApplicable.Should().BeFalse();
        dossier.Analysts.Should().NotBeNull();
    }

    private static BookFigurePosition Held(string assetClass) =>
        new(Symbol, assetClass, 1m, 100m, 150m, "binance");

    private static ValuationSnapshotResult Valuation(bool notApplicable) => new(
        Symbol,
        notApplicable,
        Price: 150m,
        IsStale: false,
        new ValuationMetricsDto(new MetricValue(20m), new MetricValue(18m), new MetricValue(null), new MetricValue(null)),
        ConsensusTarget: 180m,
        ImpliedUpsidePct: 20m,
        PeerSet: null,
        Sources: [],
        RetrievedAt: DateTimeOffset.UtcNow,
        TargetLow: 140m,
        TargetHigh: 200m,
        AnalystCount: 10,
        Currency: "USD");

    private static Task<AssetDossierResult> HandleAsync(
        BookFigurePosition? position,
        ValuationSnapshotResult valuation,
        Mock<IQueryHandler<GetAnalystActionsQuery, AnalystActionsResult>> analystHandler)
    {
        var book = new Mock<IBookFiguresService>();
        book.Setup(b => b.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BookFigures.Empty with { Positions = position is null ? [] : [position] });

        var valuationHandler = new Mock<IQueryHandler<GetValuationSnapshotQuery, ValuationSnapshotResult>>();
        valuationHandler
            .Setup(h => h.Handle(It.IsAny<GetValuationSnapshotQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(valuation);

        var handler = new GetAssetDossierQueryHandler(
            book.Object,
            Mock.Of<IHoldingTaxLotsReader>(),
            Mock.Of<IAssetSignalReader>(),
            Mock.Of<IQueryHandler<GetThesesQuery, IReadOnlyList<ThesisDto>>>(),
            valuationHandler.Object,
            analystHandler.Object,
            Mock.Of<IQueryHandler<GetNewsForTickerQuery, IReadOnlyList<NewsArticleDto>>>(),
            Mock.Of<IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>>>(),
            Mock.Of<IPositionCapSource>(),
            Mock.Of<IPriceHistoryService>(),
            Mock.Of<IFundamentalsService>(),
            Mock.Of<IAssetPriceTargetRepository>(),
            new PositionReturnsReader(Mock.Of<IPositionLedgerSource>(), Mock.Of<IHistoricalExchangeRateService>()));

        return handler.Handle(new GetAssetDossierQuery(Guid.NewGuid(), Symbol), CancellationToken.None);
    }
}
