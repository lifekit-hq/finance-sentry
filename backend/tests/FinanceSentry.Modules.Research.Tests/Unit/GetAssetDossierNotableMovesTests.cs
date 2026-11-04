namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Dossier;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>"Why is it moving" (#1218): a notable daily move is joined to the news and Radar signals near it.</summary>
public class GetAssetDossierNotableMovesTests
{
    private const string Symbol = "ABCD";
    private const string NoLinkMessage = "no linked news or signal found";
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly MoveDay = Today.AddDays(-5);

    [Fact]
    public async Task MoveWithMatchingNews_LinksTheArticleWithItsSourceUrl()
    {
        var article = News("Generic headline about ABCD", MoveDay, "https://news.example/abcd");

        var moves = (await HandleAsync(Closes(MoveDay, 0.08m), [article], [])).NotableMoves!;

        var move = moves.Moves.Should().ContainSingle().Subject;
        move.Date.Should().Be(MoveDay);
        move.ReturnPct.Should().Be(8m);
        var link = move.Links.Should().ContainSingle().Subject;
        link.Kind.Should().Be(MoveLinkKind.News);
        link.Url.Should().Be("https://news.example/abcd");
        link.Title.Should().Be(article.Title);
    }

    [Fact]
    public async Task MoveWithMatchingRadarSignal_LinksTheSignal()
    {
        var signal = Signal(MoveDay.AddDays(1), "Notable");

        var moves = (await HandleAsync(Closes(MoveDay, -0.07m), [], [signal])).NotableMoves!;

        var move = moves.Moves.Should().ContainSingle().Subject;
        move.ReturnPct.Should().Be(-7m);
        var link = move.Links.Should().ContainSingle().Subject;
        link.Kind.Should().Be(MoveLinkKind.Signal);
        link.Severity.Should().Be("Notable");
        link.Url.Should().BeNull("a signal lives in the dossier's Radar section, it has no external source");
    }

    [Fact]
    public async Task MoveWithNothingNearIt_HasNoLinks_SoThePageCanSay()
    {
        var farNews = News("Old story", MoveDay.AddDays(-9), "https://news.example/old");
        var farSignal = Signal(MoveDay.AddDays(4), "Alerted");

        var moves = (await HandleAsync(Closes(MoveDay, 0.09m), [farNews], [farSignal])).NotableMoves!;

        moves.Moves.Should().ContainSingle().Which.Links.Should().BeEmpty(NoLinkMessage);
    }

    [Fact]
    public async Task OlderMove_OfAHeavilyCoveredTicker_StillFindsItsNews_AndSignal()
    {
        const int NewerThanAnyOldCap = 150;
        var olderMove = Today.AddDays(-20);
        var ownStory = News("ABCD gaps on guidance", olderMove, "https://news.example/older");
        var ownSignal = Signal(olderMove, "Notable");
        var newerNews = Enumerable.Range(0, NewerThanAnyOldCap)
            .Select(i => News($"Later story {i}", Today.AddDays(-10 + i % 10), $"https://news.example/later/{i}"));
        var newerSignals = Enumerable.Range(0, NewerThanAnyOldCap)
            .Select(i => Signal(Today.AddDays(-10 + i % 10), "Info"));
        var history = new PriceHistory("USD", [new DailyClose(olderMove.AddDays(-1), 100m), new DailyClose(olderMove, 110m)]);

        var moves = (await HandleAsync(history, [ownStory, .. newerNews], [ownSignal, .. newerSignals])).NotableMoves!;

        var links = moves.Moves.Should().ContainSingle().Which.Links;
        links.Select(l => l.Url ?? l.Kind).Should().BeEquivalentTo("https://news.example/older", MoveLinkKind.Signal);
    }

    [Fact]
    public async Task MoveOnTheFirstVisibleDay_FindsASignalFromTheDayBeforeIt()
    {
        var firstVisibleDay = Today.AddDays(-new NotableMoveOptions().VisibleDays);
        var dayBefore = Signal(firstVisibleDay.AddDays(-1), "Alerted");
        var dayBeforeNews = News("ABCD pre-announcement", firstVisibleDay.AddDays(-1), "https://news.example/edge");

        var moves = (await HandleAsync(Closes(firstVisibleDay, 0.1m), [dayBeforeNews], [dayBefore])).NotableMoves!;

        moves.Moves.Should().ContainSingle().Which.Links.Select(l => l.Kind)
            .Should().BeEquivalentTo(MoveLinkKind.News, MoveLinkKind.Signal);
    }

    [Fact]
    public async Task MoveBelowTheThreshold_IsNotListed_AndTheNewsIsNotRead()
    {
        var newsHandler = new Mock<IQueryHandler<GetNewsForTickerQuery, IReadOnlyList<NewsArticleDto>>>();

        var dossier = await HandleAsync(
            Closes(MoveDay, 0.049m), [News("Quiet day", MoveDay, "https://news.example/q")], [Signal(MoveDay, "Alerted")], newsHandler);

        dossier.NotableMoves!.Moves.Should().BeEmpty();
        newsHandler.Verify(
            n => n.Handle(It.Is<GetNewsForTickerQuery>(q => q.Since != null), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ThresholdIsConfigurable()
    {
        var options = new NotableMoveOptions { ThresholdPct = 2m };

        var dossier = await HandleAsync(Closes(MoveDay, 0.03m), [], [], options: options);

        dossier.NotableMoves!.ThresholdPct.Should().Be(2m);
        dossier.NotableMoves.Moves.Should().ContainSingle();
    }

    [Fact]
    public async Task MoveOlderThanTheVisibleWindow_IsNotListed()
    {
        var dossier = await HandleAsync(Closes(Today.AddDays(-40), 0.2m), [], []);

        dossier.NotableMoves!.Moves.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutVerdicts_TheMovesAreStillThere_NewsAndRadarAreReadableByEveryCaller()
    {
        var dossier = await HandleAsync(
            Closes(MoveDay, 0.08m), [News("ABCD jumps", MoveDay, "https://news.example/x")], [], includeVerdicts: false);

        dossier.Scorecard.Should().BeNull();
        dossier.NotableMoves!.Moves.Should().ContainSingle().Which.Links.Should().ContainSingle();
    }

    [Fact]
    public async Task NoPriceHistory_LeavesTheSectionNull()
    {
        var dossier = await HandleAsync(new PriceHistory(null, []), [], []);

        dossier.NotableMoves.Should().BeNull();
    }

    [Fact]
    public void Links_AreRankedByTimeProximityThenRelevance()
    {
        var closes = new[] { (MoveDay.AddDays(-1), 100m), (MoveDay, 110m) };
        var sameDayRoundup = News("Market roundup", MoveDay, "https://news.example/a", "ABCD", "WXYZ");
        var sameDayAbout = News("ABCD jumps", MoveDay, "https://news.example/b");
        var nextDayAlerted = Signal(MoveDay.AddDays(1), "Alerted");

        var links = NotableMoveLinker
            .Build(Symbol, closes, [sameDayRoundup, sameDayAbout], [nextDayAlerted], MoveDay.AddDays(-5), 5m, 1, 30, 5)
            .Moves.Single().Links;

        links.Select(l => l.Url ?? l.Kind).Should().Equal("https://news.example/b", "https://news.example/a", MoveLinkKind.Signal);
    }

    private static NewsArticleDto News(string title, DateOnly day, string url, params string[] tickers) =>
        new(Guid.NewGuid(), "example-wire", title, url, null, tickers.Length == 0 ? [Symbol] : tickers, [],
            new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));

    private static DossierSignalItem Signal(DateOnly day, string severity) =>
        new(new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero), "example-scanner", "volume_spike",
            severity, new Dictionary<string, object>());

    private static PriceHistory Closes(DateOnly moveDay, decimal dailyReturn) =>
        new("USD", [new DailyClose(moveDay.AddDays(-1), 100m), new DailyClose(moveDay, 100m * (1m + dailyReturn))]);

    private static Task<AssetDossierResult> HandleAsync(
        PriceHistory history,
        IReadOnlyList<NewsArticleDto> news,
        IReadOnlyList<DossierSignalItem> signals,
        Mock<IQueryHandler<GetNewsForTickerQuery, IReadOnlyList<NewsArticleDto>>>? newsHandler = null,
        NotableMoveOptions? options = null,
        bool includeVerdicts = true)
    {
        var newsMock = newsHandler ?? new Mock<IQueryHandler<GetNewsForTickerQuery, IReadOnlyList<NewsArticleDto>>>();
        newsMock
            .Setup(n => n.Handle(It.IsAny<GetNewsForTickerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetNewsForTickerQuery q, CancellationToken _) => (IReadOnlyList<NewsArticleDto>)news
                .Where(a => (q.Since is null || a.PublishedAt >= q.Since) && (q.Until is null || a.PublishedAt < q.Until))
                .OrderByDescending(a => a.PublishedAt)
                .Take(q.Limit)
                .ToList());
        var signalReader = new Mock<IAssetSignalReader>();
        signalReader
            .Setup(s => s.GetRecentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(signals);
        signalReader
            .Setup(s => s.GetSinceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, DateOnly since, int limit, CancellationToken _) => (IReadOnlyList<DossierSignalItem>)signals
                .Where(s => DateOnly.FromDateTime(s.Timestamp.UtcDateTime) >= since)
                .OrderByDescending(s => s.Timestamp)
                .Take(limit)
                .ToList());
        var priceHistory = new Mock<IPriceHistoryService>();
        priceHistory
            .Setup(p => p.GetAsync(Symbol, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);

        var handler = new GetAssetDossierQueryHandler(
            Mock.Of<IBookFiguresService>(),
            Mock.Of<IHoldingTaxLotsReader>(),
            signalReader.Object,
            Mock.Of<IQueryHandler<GetThesesQuery, IReadOnlyList<ThesisDto>>>(),
            Mock.Of<IQueryHandler<GetValuationSnapshotQuery, ValuationSnapshotResult>>(),
            Mock.Of<IQueryHandler<GetAnalystActionsQuery, AnalystActionsResult>>(),
            newsMock.Object,
            Mock.Of<IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>>>(),
            Mock.Of<IPositionCapSource>(),
            priceHistory.Object,
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
            NullLogger<GetAssetDossierQueryHandler>.Instance,
            Options.Create(options ?? new NotableMoveOptions()));

        return handler.Handle(new GetAssetDossierQuery(Guid.NewGuid(), Symbol, includeVerdicts), CancellationToken.None);
    }
}
