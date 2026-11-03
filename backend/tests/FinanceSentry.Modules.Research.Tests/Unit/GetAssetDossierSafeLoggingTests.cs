namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// A dossier section that throws degrades to empty (feature 421) but must say which section failed,
/// so a missing card is traceable instead of silent.
/// </summary>
public class GetAssetDossierSafeLoggingTests
{
    [Fact]
    public async Task Handle_ASectionThrows_LogsAWarningNamingItAndStillReturnsTheDossier()
    {
        var thesesHandler = new Mock<IQueryHandler<GetThesesQuery, IReadOnlyList<ThesisDto>>>();
        thesesHandler
            .Setup(h => h.Handle(It.IsAny<GetThesesQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var logger = new Mock<ILogger<GetAssetDossierQueryHandler>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var handler = new GetAssetDossierQueryHandler(
            Mock.Of<IBookFiguresService>(),
            Mock.Of<IHoldingTaxLotsReader>(),
            Mock.Of<IAssetSignalReader>(),
            thesesHandler.Object,
            Mock.Of<IQueryHandler<GetValuationSnapshotQuery, ValuationSnapshotResult>>(),
            Mock.Of<IQueryHandler<GetAnalystActionsQuery, AnalystActionsResult>>(),
            Mock.Of<IQueryHandler<GetNewsForTickerQuery, IReadOnlyList<NewsArticleDto>>>(),
            Mock.Of<IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>>>(),
            Mock.Of<IPositionCapSource>(),
            Mock.Of<IPriceHistoryService>(),
            Mock.Of<IFundamentalsService>(),
            Mock.Of<IAssetPriceTargetRepository>(),
            Mock.Of<IRecommendationRepository>(),
            new PositionReturnsReader(Mock.Of<IPositionLedgerSource>(), Mock.Of<IHistoricalExchangeRateService>()),
            new ScorecardReader(
                Mock.Of<IMarketStructureReader>(),
                Mock.Of<IFundamentalsService>(),
                Mock.Of<IMarketRegimeSource>(),
                Options.Create(new OpportunityOptions()),
                Options.Create(new VerdictOptions())),
            logger.Object);

        var dossier = await handler.Handle(new GetAssetDossierQuery(Guid.NewGuid(), "ABCD"), CancellationToken.None);

        dossier.Thesis.Should().BeNull();
        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => $"{state}".Contains("theses")),
                It.Is<Exception>(e => e != null && e.Message == "boom"),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
