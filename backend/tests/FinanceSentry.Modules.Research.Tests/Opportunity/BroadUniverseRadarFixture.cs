namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

/// <summary>
/// The Radar half of the #558 story, built the way production builds it: the real
/// <see cref="RadarUniverseService"/> composes a universe from a held name, the seed ETFs and a
/// broad-universe constituent, bars are persisted through the real repository, and structure is read
/// through the real <see cref="MarketStructureReader"/> over an in-memory Radar database.
///
/// The constituent compounds fastest and the holding lags the benchmark, so momentum ordering is
/// unambiguous: any nomination the constituent wins, it wins by genuinely out-ranking the book.
/// </summary>
internal sealed class BroadUniverseRadarFixture : IAsyncDisposable
{
    public const string Benchmark = "SPY";
    public const string Sector = "XLK";

    /// <summary>Neither held nor watchlisted — it exists in the universe only as a constituent.</summary>
    public const string Constituent = "CNST";

    /// <summary>The book's laggard: present so the constituent has to out-rank something real.</summary>
    public const string Holding = "HELD";

    private const int SeriesLength = 140;

    /// <summary>Series run up to today — the reader's RS and freshness windows anchor on the current date.</summary>
    private static readonly DateOnly SeriesStart =
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(SeriesLength - 1));

    /// <summary>The single position in the book, as both Radar and the scorer's IPS fit see it.</summary>
    public static readonly BrokerageHoldingSummary HeldPosition =
        new(Holding, "STK", 10m, 1_000m, DateTime.UtcNow, "IBKR");

    private readonly RadarDbContext db;

    private BroadUniverseRadarFixture(RadarDbContext db, MarketStructureReader reader)
    {
        this.db = db;
        Reader = reader;
    }

    /// <summary>The real reader, over the composed and bar-backed universe.</summary>
    public MarketStructureReader Reader { get; }

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
        var constituents = new Mock<IIndexConstituentSource>();
        constituents.Setup(c => c.GetConstituents()).Returns([Constituent]);

        var universeService = new RadarUniverseService(
            universeRepo, brokerage.Object, watchlist.Object, banking.Object, radarOptions, constituents.Object);
        var members = await universeService.SyncAsync(CancellationToken.None);

        members.Single(m => m.Ticker == Constituent).Kind.Should().Be(
            UniverseKind.IndexConstituent, "the seam starts with the kind the composition actually persisted");
        members.Single(m => m.Ticker == Holding).Kind.Should().Be(UniverseKind.Holding);

        await barRepo.UpsertRangeAsync(Series(Benchmark, 0.30m), CancellationToken.None);
        await barRepo.UpsertRangeAsync(Series(Sector, 0.45m), CancellationToken.None);
        await barRepo.UpsertRangeAsync(Series(Constituent, 0.90m), CancellationToken.None);
        await barRepo.UpsertRangeAsync(Series(Holding, 0.05m), CancellationToken.None);

        var reader = new MarketStructureReader(
            new StructureQueryService(barRepo, universeRepo, new RadarSignalRepository(db), radarOptions),
            barRepo,
            universeRepo);

        return new BroadUniverseRadarFixture(db, reader);
    }

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
