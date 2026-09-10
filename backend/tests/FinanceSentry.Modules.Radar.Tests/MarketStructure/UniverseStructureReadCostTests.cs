namespace FinanceSentry.Modules.Radar.Tests.MarketStructure;

using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Xunit;

/// <summary>
/// Reading structure for the whole universe (#558) must resolve sector rotation and sector affinity
/// once per run, not once per member: with the S&amp;P 500 in the universe the per-member variant cost
/// thousands of bar reads a scan. The rank itself must stay identical to the per-member computation.
/// </summary>
public sealed class UniverseStructureReadCostTests
{
    private const string Benchmark = "SPY";
    private const string LeadingSector = "XLK";
    private const string LaggingSector = "XLE";
    private const int SeriesLength = 120;

    /// <summary>Series run up to today: the reader's windows are anchored on the current date.</summary>
    private static readonly DateOnly SeriesStart =
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(SeriesLength - 1));

    [Fact]
    public async Task SectorBarsAreReadOncePerRun_NotOncePerMember()
    {
        var small = await ReadUniverseAsync(memberCount: 2);
        var large = await ReadUniverseAsync(memberCount: 8);

        small.Entries.Should().HaveCount(5, "benchmark + both sectors + members all carry structure");
        large.Entries.Should().HaveCount(11);
        small.SectorReads.Should().BeGreaterThan(0, "the sector's bars are still read — just once");
        large.SectorReads.Should().Be(
            small.SectorReads,
            "rotation and affinity are universe-wide facts, so their cost must not scale with the universe");
        large.UniverseListings.Should().Be(
            small.UniverseListings,
            "the active-member list is read once per run");
    }

    [Fact]
    public async Task AffinityAssignsTheCorrelatedSectorsRank_NotTheLeadingSectors()
    {
        var read = await ReadUniverseAsync(memberCount: 2);

        var leader = read.Entries.Single(e => e.Ticker == LeadingSector);
        var laggard = read.Entries.Single(e => e.Ticker == LaggingSector);
        leader.IsEtfLens.Should().BeTrue();
        leader.Snapshot.SectorRank.Should().Be(1, "a sector ETF ranks as itself");
        laggard.Snapshot.SectorRank.Should().Be(2);

        // Members share XLE's return path, so picking the wrong sector would show up as rank 1.
        read.Entries.Single(e => e.Ticker == "AAA0").Snapshot.SectorRank.Should().Be(2);
        read.Entries.Single(e => e.Ticker == "AAA1").Snapshot.SectorRank.Should().Be(2);
    }

    private static async Task<UniverseRead> ReadUniverseAsync(int memberCount)
    {
        await using var db = TestSupport.NewContext();

        var barRepo = new CountingDailyBarRepository(new DailyBarRepository(db));
        var universeRepo = new CountingUniverseRepository(new RadarUniverseRepository(db));

        var members = new List<RadarUniverseMember>
        {
            Member(Benchmark, UniverseKind.Benchmark),
            Member(LeadingSector, UniverseKind.Sector),
            Member(LaggingSector, UniverseKind.Sector),
        };
        var holdings = Enumerable.Range(0, memberCount).Select(i => Member($"AAA{i}", UniverseKind.Holding)).ToList();
        members.AddRange(holdings);
        await universeRepo.UpsertMembersAsync(members, CancellationToken.None);

        // Both sectors outrun the benchmark, the leader by more — so they rank 1 and 2. Their gains
        // arrive on different days, and the holdings step exactly with the laggard, so
        // return-correlation affinity has one right answer and a visibly wrong one.
        await barRepo.UpsertRangeAsync(Series(Benchmark, i => 0.1m), CancellationToken.None);
        await barRepo.UpsertRangeAsync(
            Series(LeadingSector, i => i % 2 == 0 ? 1.8m : 0.2m), CancellationToken.None);
        foreach (var ticker in holdings.Select(m => m.Ticker).Prepend(LaggingSector))
        {
            await barRepo.UpsertRangeAsync(Series(ticker, i => i % 3 == 0 ? 1.2m : 0.15m), CancellationToken.None);
        }

        var options = TestSupport.Options(new RadarOptions
        {
            Benchmark = Benchmark,
            BenchmarkTickers = [Benchmark],
            SectorTickers = [LeadingSector, LaggingSector],
            IndustryTickers = [],
            FreshnessMaxTradingDays = int.MaxValue,
        });
        var structureQueries = new StructureQueryService(
            barRepo, universeRepo, new RadarSignalRepository(db), options);
        var reader = new MarketStructureReader(structureQueries, barRepo, universeRepo);

        barRepo.ReadsByTicker.Clear();
        universeRepo.ActiveListings = 0;

        var entries = await reader.GetUniverseStructuresAsync(CancellationToken.None);

        return new UniverseRead(
            entries,
            barRepo.ReadsByTicker.TryGetValue(LeadingSector, out var sectorReads) ? sectorReads : 0,
            universeRepo.ActiveListings);
    }

    private static RadarUniverseMember Member(string ticker, UniverseKind kind) => new()
    {
        Ticker = ticker, Kind = kind, Source = UniverseSource.Auto, Active = true,
    };

    /// <summary>Series whose bar-to-bar gain is <paramref name="dailyGain"/> of the bar's index.</summary>
    private static List<DailyBar> Series(string ticker, Func<int, decimal> dailyGain)
    {
        var bars = new List<DailyBar>();
        var price = 100m;
        for (var i = 0; i < SeriesLength; i++)
        {
            price += dailyGain(i);
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

    private sealed record UniverseRead(
        IReadOnlyList<Core.Interfaces.UniverseStructureEntry> Entries, int SectorReads, int UniverseListings);

    /// <summary>Counts bar reads per ticker so a per-member fan-out cannot creep back in unnoticed.</summary>
    private sealed class CountingDailyBarRepository(IDailyBarRepository inner) : IDailyBarRepository
    {
        public Dictionary<string, int> ReadsByTicker { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<int> UpsertRangeAsync(IReadOnlyCollection<DailyBar> bars, CancellationToken ct = default)
            => inner.UpsertRangeAsync(bars, ct);

        public Task<IReadOnlyList<DailyBar>> GetSinceAsync(string ticker, DateOnly since, CancellationToken ct = default)
        {
            this.ReadsByTicker[ticker] = this.ReadsByTicker.TryGetValue(ticker, out var count) ? count + 1 : 1;
            return inner.GetSinceAsync(ticker, since, ct);
        }

        public Task<DateOnly?> GetLatestDateAsync(string ticker, CancellationToken ct = default)
            => inner.GetLatestDateAsync(ticker, ct);

        public Task<IReadOnlyDictionary<string, DateOnly>> GetLatestDatesAsync(
            IReadOnlyCollection<string> tickers, CancellationToken ct = default)
            => inner.GetLatestDatesAsync(tickers, ct);
    }

    private sealed class CountingUniverseRepository(IRadarUniverseRepository inner) : IRadarUniverseRepository
    {
        public int ActiveListings { get; set; }

        public Task<IReadOnlyList<RadarUniverseMember>> ListActiveAsync(CancellationToken ct = default)
        {
            this.ActiveListings++;
            return inner.ListActiveAsync(ct);
        }

        public Task<IReadOnlyList<RadarUniverseMember>> ListAllAsync(CancellationToken ct = default)
            => inner.ListAllAsync(ct);

        public Task UpsertMembersAsync(IReadOnlyCollection<RadarUniverseMember> members, CancellationToken ct = default)
            => inner.UpsertMembersAsync(members, ct);

        public Task DeactivateAsync(IReadOnlyCollection<string> tickers, CancellationToken ct = default)
            => inner.DeactivateAsync(tickers, ct);
    }
}
