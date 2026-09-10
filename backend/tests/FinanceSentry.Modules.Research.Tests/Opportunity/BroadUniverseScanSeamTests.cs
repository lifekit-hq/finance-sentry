namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// The whole point of #558: a name Denys neither holds nor watches must be able to win a Scan slot.
/// Both halves of that were unit-tested in isolation — Radar's universe composition against a mocked
/// constituent source, Research's rules against hand-built <see cref="UniverseStructureEntry"/>
/// values — which left the seam between them untested: nothing proved that a persisted
/// <see cref="UniverseKind.IndexConstituent"/> member actually comes back out of the real
/// <see cref="MarketStructureReader.GetUniverseStructuresAsync"/> as a non-ETF-lens, rankable entry.
/// These tests drive the real composition → persistence → structure-read → nomination chain end to
/// end, over an in-memory Radar database.
/// </summary>
public sealed class BroadUniverseScanSeamTests
{
    private const string Benchmark = "SPY";
    private const string Sector = "XLK";

    /// <summary>Neither held nor watchlisted — it exists in the universe only as a constituent.</summary>
    private const string Constituent = "CNST";

    /// <summary>The book's laggard: present so the constituent has to out-rank something real.</summary>
    private const string Holding = "HELD";

    private const int SeriesLength = 140;
    private const int ConstituentGrade = 91;
    private const int HoldingGrade = 40;

    /// <summary>Series run up to today — the reader's RS and freshness windows anchor on the current date.</summary>
    private static readonly DateOnly SeriesStart =
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(SeriesLength - 1));

    [Fact]
    public async Task AnIndexConstituentSurfacesAsARankableNonEtfLensEntry()
    {
        var universe = await ReadUniverseStructuresAsync();

        var entry = universe.Should().ContainSingle(e => e.Ticker == Constituent).Subject;
        entry.IsEtfLens.Should().BeFalse(
            "IndexConstituent is an ordinary ticker, not a lens the scanner reads the market through");
        entry.Snapshot.Stale.Should().BeFalse();
        entry.Snapshot.RsByWindow.Should().ContainKey(ScanNominationRules.RsWindowBars)
            .WhoseValue.Should().NotBeNull("without an RS value the ranking has no momentum half to score");

        universe.Where(e => e.IsEtfLens).Select(e => e.Ticker)
            .Should().BeEquivalentTo([Benchmark, Sector], "only the seed ETFs are lenses");
    }

    [Fact]
    public async Task AConstituentThatOutrunsTheBookIsNominatedAndTheLaggingHoldingIsNot()
    {
        var universe = await ReadUniverseStructuresAsync();

        var nominations = ScanNominationRules.Evaluate(universe, new OpportunityOptions());

        nominations.Select(n => n.Ticker).Should().BeEquivalentTo(
            [Constituent],
            "the scan must nominate a name outside the book, and must not nominate the lagging holding or the ETF lenses");
        nominations[0].RsPercentile.Should().Be(100m);
    }

    [Fact]
    public async Task TheNominatedConstituentCarriesACombinedQualityMomentumScore()
    {
        var universe = await ReadUniverseStructuresAsync();
        var options = new OpportunityOptions();

        var ranked = ScanNominationRules.RankByQualityMomentum(
            ScanNominationRules.Evaluate(universe, options),
            new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
            {
                [Constituent] = ConstituentGrade,
                [Holding] = HoldingGrade,
            },
            options);

        var top = ranked.Should().ContainSingle().Subject;
        top.Ticker.Should().Be(Constituent);
        top.QualityScore.Should().Be(ConstituentGrade);
        // grade x 0.5 + RS percentile x 0.5, the default weighting.
        top.CombinedScore.Should().Be(95.5m);
        top.Reasons.Should().Contain(ScanNominationRules.QualityMomentumReason);
    }

    /// <summary>
    /// Composes the universe through the real <see cref="RadarUniverseService"/> (so the constituent's
    /// <see cref="UniverseKind"/> is the one production would persist), then reads structure through
    /// the real <see cref="MarketStructureReader"/>.
    /// </summary>
    private static async Task<IReadOnlyList<UniverseStructureEntry>> ReadUniverseStructuresAsync()
    {
        var dbOptions = new DbContextOptionsBuilder<RadarDbContext>()
            .UseInMemoryDatabase($"broad-universe-seam-{Guid.NewGuid():N}")
            .Options;
        await using var db = new RadarDbContext(dbOptions);

        var radarOptions = Options.Create(new RadarOptions
        {
            Benchmark = Benchmark,
            BenchmarkTickers = [Benchmark],
            SectorTickers = [Sector],
            IndustryTickers = [],
            BroadUniverseEnabled = true,
            FreshnessMaxTradingDays = int.MaxValue,
        });

        var universeRepo = new RadarUniverseRepository(db);
        var barRepo = new DailyBarRepository(db);

        var userId = Guid.NewGuid();
        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([userId]);
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BrokerageHoldingSummary(Holding, "STK", 10m, 1_000m, DateTime.UtcNow, "IBKR")]);
        var watchlist = new Mock<IWatchlistReader>();
        watchlist.Setup(w => w.ListTickersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var constituents = new Mock<IIndexConstituentSource>();
        constituents.Setup(c => c.GetConstituents()).Returns([Constituent]);

        var universeService = new RadarUniverseService(
            universeRepo, brokerage.Object, watchlist.Object, banking.Object, radarOptions, constituents.Object);
        var members = await universeService.SyncAsync(CancellationToken.None);

        members.Single(m => m.Ticker == Constituent).Kind.Should().Be(
            UniverseKind.IndexConstituent, "the seam starts with the kind the composition actually persisted");
        members.Single(m => m.Ticker == Holding).Kind.Should().Be(UniverseKind.Holding);

        // The constituent compounds fastest, the holding lags the benchmark: momentum ordering is
        // unambiguous, so a nomination can only come from the constituent genuinely out-ranking the book.
        await barRepo.UpsertRangeAsync(Series(Benchmark, 0.30m), CancellationToken.None);
        await barRepo.UpsertRangeAsync(Series(Sector, 0.45m), CancellationToken.None);
        await barRepo.UpsertRangeAsync(Series(Constituent, 0.90m), CancellationToken.None);
        await barRepo.UpsertRangeAsync(Series(Holding, 0.05m), CancellationToken.None);

        var reader = new MarketStructureReader(
            new StructureQueryService(barRepo, universeRepo, new RadarSignalRepository(db), radarOptions),
            barRepo,
            universeRepo);

        return await reader.GetUniverseStructuresAsync(CancellationToken.None);
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
