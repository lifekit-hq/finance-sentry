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
    private const string SectorEtf = "XLK";
    private const int SeriesLength = 120;

    /// <summary>Series run up to today: the reader's windows are anchored on the current date.</summary>
    private static readonly DateOnly SeriesStart =
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(SeriesLength - 1));

    [Fact]
    public async Task SectorBarsAreReadOncePerRun_NotOncePerMember()
    {
        var small = await ReadUniverseAsync(memberCount: 2);
        var large = await ReadUniverseAsync(memberCount: 8);

        small.Entries.Should().HaveCount(4, "benchmark + sector + members all carry structure");
        large.Entries.Should().HaveCount(10);
        large.SectorReads.Should().Be(
            small.SectorReads,
            "rotation and affinity are universe-wide facts, so their cost must not scale with the universe");
        large.UniverseListings.Should().Be(
            small.UniverseListings,
            "the active-member list is read once per run");
    }

    [Fact]
    public async Task AffinityStillAssignsTheSectorRank()
    {
        var read = await ReadUniverseAsync(memberCount: 2);

        var member = read.Entries.Single(e => e.Ticker == "AAA0");
        member.Snapshot.SectorRank.Should().Be(1, "AAA0's returns track the only ranked sector ETF");

        var sectorLens = read.Entries.Single(e => e.Ticker == SectorEtf);
        sectorLens.IsEtfLens.Should().BeTrue();
        sectorLens.Snapshot.SectorRank.Should().Be(1, "a sector ETF ranks as itself");
    }

    private static async Task<UniverseRead> ReadUniverseAsync(int memberCount)
    {
        await using var db = TestSupport.NewContext();

        var barRepo = new CountingDailyBarRepository(new DailyBarRepository(db));
        var universeRepo = new CountingUniverseRepository(new RadarUniverseRepository(db));

        var members = new List<RadarUniverseMember>
        {
            Member(Benchmark, UniverseKind.Benchmark),
            Member(SectorEtf, UniverseKind.Sector),
        };
        members.AddRange(Enumerable.Range(0, memberCount).Select(i => Member($"AAA{i}", UniverseKind.Holding)));
        await universeRepo.UpsertMembersAsync(members, CancellationToken.None);

        // The benchmark drifts up slowly; the sector and its members share a steeper path, so
        // return-correlation affinity has a single obvious winner.
        await barRepo.UpsertRangeAsync(Series(Benchmark, step: 0.1m), CancellationToken.None);
        foreach (var member in members.Where(m => m.Ticker != Benchmark))
        {
            await barRepo.UpsertRangeAsync(Series(member.Ticker, step: 1m), CancellationToken.None);
        }

        var options = TestSupport.Options(new RadarOptions
        {
            Benchmark = Benchmark,
            BenchmarkTickers = [Benchmark],
            SectorTickers = [SectorEtf],
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
            barRepo.ReadsByTicker.TryGetValue(SectorEtf, out var sectorReads) ? sectorReads : 0,
            universeRepo.ActiveListings);
    }

    private static RadarUniverseMember Member(string ticker, UniverseKind kind) => new()
    {
        Ticker = ticker, Kind = kind, Source = UniverseSource.Auto, Active = true,
    };

    private static List<DailyBar> Series(string ticker, decimal step)
    {
        var bars = new List<DailyBar>();
        for (var i = 0; i < SeriesLength; i++)
        {
            var price = 100m + (i * step);
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
