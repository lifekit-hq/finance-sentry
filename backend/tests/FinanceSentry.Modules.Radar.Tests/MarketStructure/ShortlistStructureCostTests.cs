namespace FinanceSentry.Modules.Radar.Tests.MarketStructure;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// The #558 cost bound, at the expensive end of the funnel: a scan cycle over an index of N
/// constituents with a stage-1 shortlist of K must compute market structure for at most
/// K + |held| + |watchlist| tickers (plus the seed lenses the reader always carries).
///
/// The test seeds bars for the whole index — the state PR 612's rotating ingestion left behind — so
/// the bound has to come from what the universe admits, not from which tickers happen to have bars.
/// A regression that ranked "everything ingested" would light this up as 200 structure computations.
/// </summary>
public sealed class ShortlistStructureCostTests
{
    private const string Benchmark = "SPY";
    private const string Sector = "XLK";
    private const string Held = "HELD";
    private const string Watched = "WTCH";
    private const int IndexSize = 200;
    private const int SeriesLength = 140;

    private static readonly string[] Shortlist = ["IDX007", "IDX042", "IDX199"];

    private static readonly DateOnly SeriesStart =
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(SeriesLength - 1));

    private static readonly Guid BookOwner = Guid.NewGuid();

    [Fact]
    public async Task StructureIsComputedForTheShortlistAndTheBookOnly()
    {
        await using var db = TestSupport.NewContext();
        var (reader, bars) = await ComposeAsync(db);

        var entries = await reader.GetUniverseStructuresAsync(CancellationToken.None);

        entries.Select(e => e.Ticker).Should().BeEquivalentTo(
            new[] { Benchmark, Sector, Held, Watched }.Concat(Shortlist),
            "stage 2 ranks the shortlist and the book — never every constituent that still has bars");
        entries.Count.Should().BeLessThanOrEqualTo(
            Shortlist.Length + 2 + 2, "the bound is K + |held| + |watchlist| over the seed lenses");
        bars.ReadsByTicker.Keys.Should().NotContain(
            "IDX000", "a constituent outside the shortlist costs no bar read, ingested history or not");
    }

    [Fact]
    public async Task AConstituentOutsideTheShortlistIsNotNominatable()
    {
        await using var db = TestSupport.NewContext();
        var (reader, _) = await ComposeAsync(db);

        var entries = await reader.GetUniverseStructuresAsync(CancellationToken.None);

        entries.Should().Contain(e => e.Ticker == "IDX007" && !e.IsEtfLens);
        entries.Should().NotContain(e => e.Ticker == "IDX001");
    }

    private static async Task<(MarketStructureReader Reader, CountingDailyBarRepository Bars)> ComposeAsync(
        RadarDbContext db)
    {
        var options = TestSupport.Options(new RadarOptions
        {
            Benchmark = Benchmark,
            BenchmarkTickers = [Benchmark],
            SectorTickers = [Sector],
            IndustryTickers = [],
            BroadUniverseEnabled = true,
            FreshnessMaxTradingDays = int.MaxValue,
        });

        var barRepo = new CountingDailyBarRepository(new DailyBarRepository(db));
        var universeRepo = new RadarUniverseRepository(db);

        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([BookOwner]);
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(BookOwner, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BrokerageHoldingSummary(Held, "STK", 1m, 100m, DateTime.UtcNow, "IBKR")]);
        var watchlist = new Mock<IWatchlistReader>();
        watchlist.Setup(w => w.ListTickersAsync(BookOwner, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Watched]);

        var universeService = new RadarUniverseService(
            universeRepo, brokerage.Object, watchlist.Object, banking.Object, options,
            new FakeShortlistSource { Tickers = Shortlist });
        await universeService.SyncAsync(CancellationToken.None);

        var index = Enumerable.Range(0, IndexSize).Select(i => FormattableString.Invariant($"IDX{i:D3}"));
        foreach (var ticker in index.Concat([Benchmark, Sector, Held, Watched]))
        {
            await barRepo.UpsertRangeAsync(Series(ticker), CancellationToken.None);
        }

        barRepo.ReadsByTicker.Clear();

        var reader = new MarketStructureReader(
            new StructureQueryService(barRepo, universeRepo, new RadarSignalRepository(db), options),
            barRepo,
            universeRepo);

        return (reader, barRepo);
    }

    private static List<DailyBar> Series(string ticker)
    {
        var bars = new List<DailyBar>(SeriesLength);
        var price = 100m;
        for (var i = 0; i < SeriesLength; i++)
        {
            price += 0.25m;
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
