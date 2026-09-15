namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

/// <summary>
/// The #558 funnel's first two joints, built the way production builds them: the real
/// <see cref="ScanShortlistService"/> composes a stage-1 shortlist out of an index list, a quote read
/// and the street feed; the real <see cref="RadarUniverseService"/> composes a universe from that
/// shortlist plus the book; bars are persisted through the real repository; and structure is read
/// through the real <see cref="MarketStructureReader"/> over an in-memory Radar database.
///
/// Nothing hands the shortlist in. <see cref="Constituent"/> reaches the universe only by out-ranking
/// <see cref="Decoys"/> on stage-1 signals, so a test built on this fixture proves the funnel rather
/// than assuming it. Past that gate the constituent compounds fastest and the holding lags the
/// benchmark, so momentum ordering in stage 2 is unambiguous too: any nomination the constituent
/// wins, it wins by genuinely out-ranking the book.
/// </summary>
internal sealed class BroadUniverseRadarFixture : IAsyncDisposable
{
    public const string Benchmark = "SPY";
    public const string Sector = "XLK";

    /// <summary>Neither held nor watchlisted — it is in the universe only because stage 1 shortlisted it.</summary>
    public const string Constituent = "CNST";

    /// <summary>The book's laggard: present so the constituent has to out-rank something real.</summary>
    public const string Holding = "HELD";

    /// <summary>Revenue growth EDGAR reports for the constituent; <see cref="FundamentalsScorer"/> grades it 95.</summary>
    public const decimal ConstituentRevenueYoy = 0.45m;

    /// <summary>The grade <see cref="ConstituentRevenueYoy"/> earns — above the default top-tier bar of 80.</summary>
    public const int ConstituentGrade = 95;

    /// <summary>Index members stage 1 must cut: quoted and graded, but out-moved and out-grown.</summary>
    public static readonly string[] Decoys = ["DCOY", "DCOZ"];

    private const decimal DecoyRevenueYoy = -0.20m;
    private const int SeriesLength = 140;

    /// <summary>Series run up to today — the reader's RS and freshness windows anchor on the current date.</summary>
    private static readonly DateOnly SeriesStart =
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(SeriesLength - 1));

    /// <summary>The single position in the book, as both Radar and the scorer's IPS fit see it.</summary>
    public static readonly BrokerageHoldingSummary HeldPosition =
        new(Holding, "STK", 10m, 1_000m, DateTime.UtcNow, "IBKR");

    private readonly RadarDbContext db;

    private BroadUniverseRadarFixture(
        RadarDbContext db,
        MarketStructureReader reader,
        IReadOnlyList<string> shortlist,
        RecordingSecEdgarService edgar)
    {
        this.db = db;
        Reader = reader;
        Shortlist = shortlist;
        Edgar = edgar;
    }

    /// <summary>The real reader, over the composed and bar-backed universe.</summary>
    public MarketStructureReader Reader { get; }

    /// <summary>What stage 1 actually returned — the names stage 2 is allowed to pay bar math for.</summary>
    public IReadOnlyList<string> Shortlist { get; }

    /// <summary>
    /// The one EDGAR truth for the whole funnel. Production shares a cached singleton between stage 1
    /// and the scan job's own grading, so a fixture with two doubles could disagree with itself.
    /// </summary>
    public RecordingSecEdgarService Edgar { get; }

    public static async Task<BroadUniverseRadarFixture> CreateAsync(Guid? userId = null)
    {
        var dbOptions = new DbContextOptionsBuilder<RadarDbContext>()
            .UseInMemoryDatabase($"broad-universe-{Guid.NewGuid():N}")
            .Options;
        var db = new RadarDbContext(dbOptions);
        try
        {
            return await ComposeAsync(db, userId ?? Guid.NewGuid());
        }
        catch
        {
            // The guard assertions below can throw, and an un-owned context would leak with them.
            await db.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => db.DisposeAsync();

    private static async Task<BroadUniverseRadarFixture> ComposeAsync(RadarDbContext db, Guid bookOwner)
    {
        var radarOptions = Options.Create(new RadarOptions
        {
            Benchmark = Benchmark,
            BenchmarkTickers = [Benchmark],
            SectorTickers = [Sector],
            IndustryTickers = [],
            BroadUniverseEnabled = true,
        });

        var universeRepo = new RadarUniverseRepository(db);
        var barRepo = new DailyBarRepository(db);

        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([bookOwner]);
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(bookOwner, It.IsAny<CancellationToken>()))
            .ReturnsAsync([HeldPosition]);
        var watchlist = new Mock<IWatchlistReader>();
        watchlist.Setup(w => w.ListTickersAsync(bookOwner, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var edgar = StageOneEdgar();
        var shortlistSource = StageOne(edgar);
        var shortlist = await shortlistSource.GetShortlistAsync(CancellationToken.None);
        shortlist.Should().Equal(
            [Constituent],
            "the fixture's whole claim is that this name entered through stage 1, and the decoys did not");

        var universeService = new RadarUniverseService(
            universeRepo, brokerage.Object, watchlist.Object, banking.Object, radarOptions, shortlistSource);
        var members = await universeService.SyncAsync(CancellationToken.None);

        members.Single(m => m.Ticker == Constituent).Kind.Should().Be(
            UniverseKind.IndexConstituent, "the seam starts with the kind the composition actually persisted");
        members.Single(m => m.Ticker == Holding).Kind.Should().Be(UniverseKind.Holding);
        members.Select(m => m.Ticker).Should().NotIntersectWith(
            Decoys, "a name stage 1 cut never costs stage 2 a bar fetch or a structure computation");

        await barRepo.UpsertRangeAsync(Series(Benchmark, 0.30m), CancellationToken.None);
        await barRepo.UpsertRangeAsync(Series(Sector, 0.45m), CancellationToken.None);
        await barRepo.UpsertRangeAsync(Series(Constituent, 0.90m), CancellationToken.None);
        await barRepo.UpsertRangeAsync(Series(Holding, 0.05m), CancellationToken.None);

        var reader = new MarketStructureReader(
            new StructureQueryService(barRepo, universeRepo, new RadarSignalRepository(db), radarOptions),
            barRepo,
            universeRepo);

        return new BroadUniverseRadarFixture(db, reader, shortlist, edgar);
    }

    /// <summary>
    /// Stage 1 over a three-name index: the constituent is the day's strongest mover, the only name the
    /// street acted on, and the only one growing revenue, so it wins the single shortlist slot on every
    /// signal the stage reads. The decoys are quoted and gradeable — they lose the cut, they do not
    /// dodge it.
    /// </summary>
    private static ScanShortlistService StageOne(ISecEdgarService edgar)
    {
        var constituents = new Mock<IIndexConstituentSource>();
        constituents.Setup(c => c.GetConstituents()).Returns([Constituent, .. Decoys]);

        var marketData = new Mock<IMarketDataService>();
        marketData.Setup(m => m.GetQuotesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, QuoteCacheEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [Constituent] = Quote(Constituent, percentChange: 5m),
                [Decoys[0]] = Quote(Decoys[0], percentChange: 1m),
                [Decoys[1]] = Quote(Decoys[1], percentChange: 0m),
            });

        var street = new Mock<IAnalystActionRepository>();
        street.Setup(s => s.QueryAsync(
                null, It.IsAny<DateOnly>(), null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AnalystAction
                {
                    Ticker = Constituent,
                    Firm = "Test Capital",
                    ActionType = AnalystActionType.Upgrade,
                    ActionDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    Source = "test",
                },
            ]);

        return new ScanShortlistService(
            constituents.Object,
            marketData.Object,
            street.Object,
            edgar,
            Options.Create(new OpportunityOptions { ScanShortlistSize = 1 }),
            NullLogger<ScanShortlistService>.Instance);
    }

    private static RecordingSecEdgarService StageOneEdgar()
        => new(new Dictionary<string, IReadOnlyList<FundamentalFact>>(StringComparer.OrdinalIgnoreCase)
        {
            [Constituent] = RevenueGrowthFacts(Constituent, ConstituentRevenueYoy),
            [Decoys[0]] = RevenueGrowthFacts(Decoys[0], DecoyRevenueYoy),
            [Decoys[1]] = RevenueGrowthFacts(Decoys[1], DecoyRevenueYoy),
        });

    /// <summary>Two comparable quarters, so <see cref="FundamentalsScorer"/> has a year-on-year to score.</summary>
    private static IReadOnlyList<FundamentalFact> RevenueGrowthFacts(string ticker, decimal revenueYoy)
        => [
            new(ticker, "Revenue", "Revenue", "USD", 100m * (1m + revenueYoy), new DateOnly(2026, 5, 31), "Q2", 2026, "10-Q"),
            new(ticker, "Revenue", "Revenue", "USD", 100m, new DateOnly(2025, 5, 31), "Q2", 2025, "10-Q"),
        ];

    private static QuoteCacheEntry Quote(string ticker, decimal percentChange)
        => new() { Ticker = ticker, PreviousClose = 100m, Price = 100m + percentChange };

    private static List<DailyBar> Series(string ticker, decimal dailyGain)
    {
        var bars = new List<DailyBar>(SeriesLength);
        var price = 100m;
        for (var i = 0; i < SeriesLength; i++)
        {
            price += dailyGain;
            bars.Add(new DailyBar
            {
                Ticker = ticker,
                Date = SeriesStart.AddDays(i),
                Open = price,
                High = price + 1m,
                Low = price - 1m,
                Close = price,
                AdjClose = price,
                Volume = 1_000_000,
            });
        }

        return bars;
    }
}
