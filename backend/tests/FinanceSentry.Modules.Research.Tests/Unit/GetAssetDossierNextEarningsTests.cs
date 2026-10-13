namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetAssetDossierNextEarningsTests
{
    private const string Symbol = "AAPL";
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task NextEarnings_ExDividendBeforeEarnings_ReturnsTheEarningsEvent()
    {
        var earnings = Event(EarningsEventType.Earnings, 30);

        var dossier = await HandleAsync(Event(EarningsEventType.ExDividend, 10), earnings);

        dossier.NextEarnings.Should().Be(earnings);
    }

    [Fact]
    public async Task NextEarnings_OnlyNonEarningsEvents_IsNull()
    {
        var dossier = await HandleAsync(
            Event(EarningsEventType.ExDividend, 10), Event(EarningsEventType.Dividend, 20));

        dossier.NextEarnings.Should().BeNull();
    }

    private static EarningsEventDto Event(string type, int daysAhead) =>
        new(Symbol, type, Today.AddDays(daysAhead), false, "yahoo");

    private static Task<AssetDossierResult> HandleAsync(params EarningsEventDto[] events)
    {
        // Mirrors the real calendar service: an EventType filter keeps only that type, null keeps all.
        var calendar = new Mock<IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>>>();
        calendar
            .Setup(c => c.Handle(It.IsAny<GetEarningsCalendarQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetEarningsCalendarQuery q, CancellationToken _) =>
                (IReadOnlyList<EarningsEventDto>)events
                    .Where(e => q.EventType is null || e.EventType == q.EventType)
                    .ToList());

        var handler = new GetAssetDossierQueryHandler(
            Mock.Of<IBookFiguresService>(),
            Mock.Of<IHoldingTaxLotsReader>(),
            Mock.Of<IAssetSignalReader>(),
            Mock.Of<IQueryHandler<GetThesesQuery, IReadOnlyList<ThesisDto>>>(),
            Mock.Of<IQueryHandler<GetValuationSnapshotQuery, ValuationSnapshotResult>>(),
            Mock.Of<IQueryHandler<GetAnalystActionsQuery, AnalystActionsResult>>(),
            Mock.Of<IQueryHandler<GetNewsForTickerQuery, IReadOnlyList<NewsArticleDto>>>(),
            calendar.Object,
            Mock.Of<IPositionCapSource>(),
            Mock.Of<IPriceHistoryService>(),
            Mock.Of<IFundamentalsService>(),
            Mock.Of<IAssetPriceTargetRepository>(),
            Mock.Of<IRecommendationRepository>(),
            new PositionReturnsReader(Mock.Of<IPositionLedgerSource>(), Mock.Of<IHistoricalExchangeRateService>()));

        return handler.Handle(new GetAssetDossierQuery(Guid.NewGuid(), Symbol), CancellationToken.None);
    }
}
