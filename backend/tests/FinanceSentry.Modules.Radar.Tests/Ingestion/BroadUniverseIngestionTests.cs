namespace FinanceSentry.Modules.Radar.Tests.Ingestion;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Commands;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Broad-universe ingestion budget (#558): a 500-name universe must not turn the nightly run into a
/// 500-call fan-out, and the book's freshness must never be traded away for breadth.
/// </summary>
public sealed class BroadUniverseIngestionTests
{
    private static readonly DateOnly SeriesStart = new(2024, 1, 1);

    [Fact]
    public async Task CoreMembersAreIngestedFirst_AndConstituentsFillTheRemainingBudget()
    {
        var members = new[]
        {
            Member("AAPL", UniverseKind.Holding),
            Member("TSLA", UniverseKind.Watchlist),
            Member("SPY", UniverseKind.Benchmark),
            Member("AAA", UniverseKind.IndexConstituent),
            Member("BBB", UniverseKind.IndexConstituent),
            Member("CCC", UniverseKind.IndexConstituent),
        };

        var (source, summary) = await RunAsync(members, budget: 2);

        summary.Errors.Should().Be(0);
        source.Requested.Should().Equal("AAPL", "TSLA", "SPY", "AAA", "BBB");
    }

    [Fact]
    public async Task ConstituentsRotate_LeastFreshFirst()
    {
        var members = new[]
        {
            Member("AAA", UniverseKind.IndexConstituent),
            Member("BBB", UniverseKind.IndexConstituent),
            Member("CCC", UniverseKind.IndexConstituent),
        };

        // AAA is current, BBB is a week behind, CCC has never been ingested.
        var seeded = new Dictionary<string, int> { ["AAA"] = 30, ["BBB"] = 23 };

        var (source, _) = await RunAsync(members, budget: 2, seededBarCounts: seeded);

        source.Requested.Should().Equal("CCC", "BBB");
        source.Requested.Should().NotContain("AAA", "the freshest constituent waits for the next run");
    }

    [Fact]
    public async Task ZeroBudget_LeavesTheUniverseAtCoreMembers()
    {
        var members = new[]
        {
            Member("AAPL", UniverseKind.Holding),
            Member("AAA", UniverseKind.IndexConstituent),
        };

        var (source, _) = await RunAsync(members, budget: 0);

        source.Requested.Should().Equal("AAPL");
    }

    private static RadarUniverseMember Member(string ticker, UniverseKind kind) => new()
    {
        Ticker = ticker, Kind = kind, Source = UniverseSource.Auto, Active = true,
    };

    private static IReadOnlyList<DailyBarData> Series(int count)
    {
        var bars = new List<DailyBarData>();
        for (var i = 0; i < count; i++)
        {
            var price = 100m + i;
            bars.Add(new DailyBarData(SeriesStart.AddDays(i), price, price + 1, price - 1, price, price, 1_000_000));
        }

        return bars;
    }

    private static async Task<(FakeHistorySource Source, IngestRunSummary Summary)> RunAsync(
        IReadOnlyList<RadarUniverseMember> members,
        int budget,
        IReadOnlyDictionary<string, int>? seededBarCounts = null)
    {
        var barsByTicker = members.ToDictionary(m => m.Ticker, _ => Series(40));

        await using var db = TestSupport.NewContext();
        var repo = new DailyBarRepository(db);

        foreach (var (ticker, count) in seededBarCounts ?? new Dictionary<string, int>())
        {
            await repo.UpsertRangeAsync(
                Series(count).Select(b => new DailyBar
                {
                    Ticker = ticker,
                    Date = b.Date,
                    Open = b.Open,
                    High = b.High,
                    Low = b.Low,
                    Close = b.Close,
                    AdjClose = b.AdjClose,
                    Volume = b.Volume,
                }).ToList(),
                CancellationToken.None);
        }

        var source = new FakeHistorySource(barsByTicker);
        var handler = new IngestDailyBarsCommandHandler(
            new FakeUniverseService(members),
            source,
            repo,
            TestSupport.Options(new RadarOptions { BroadUniverseMaxIngestPerRun = budget }),
            NullLogger<IngestDailyBarsCommandHandler>.Instance);

        var summary = await handler.Handle(new IngestDailyBarsCommand(), CancellationToken.None);
        return (source, summary);
    }
}
