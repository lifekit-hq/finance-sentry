using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Commands;
using FinanceSentry.Modules.Radar.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceSentry.Modules.Radar.Tests.Ingestion;

public sealed class IngestDailyBarsIdempotencyTests
{
    private static IReadOnlyList<DailyBarData> Series(DateOnly start, int count)
    {
        var bars = new List<DailyBarData>();
        for (var i = 0; i < count; i++)
        {
            var d = start.AddDays(i);
            var price = 100m + i;
            bars.Add(new DailyBarData(d, price, price + 1, price - 1, price, price, 1_000_000 + i));
        }

        return bars;
    }

    private static RadarUniverseMember Member(string ticker) => new()
    {
        Ticker = ticker, Kind = UniverseKind.Holding, Source = UniverseSource.Auto, Active = true,
    };

    [Fact]
    public async Task EmptyStore_IngestsBarsBackToLookback()
    {
        var start = new DateOnly(2024, 1, 1);
        var barsByTicker = new Dictionary<string, IReadOnlyList<DailyBarData>>
        {
            ["AAA"] = Series(start, 210),
        };

        await using var db = TestSupport.NewContext();
        var repo = new InMemoryDailyBarRepository(db);
        var handler = new IngestDailyBarsCommandHandler(
            new FakeUniverseService([Member("AAA")]),
            new FakeHistorySource(barsByTicker),
            repo,
            TestSupport.Options(),
            NullLogger<IngestDailyBarsCommandHandler>.Instance);

        var summary = await handler.Handle(new IngestDailyBarsCommand(), CancellationToken.None);

        summary.BarsAdded.Should().Be(210);
        summary.Errors.Should().Be(0);
        (await repo.GetSinceAsync("AAA", start, CancellationToken.None)).Should().HaveCount(210);
    }

    [Fact]
    public async Task ReRun_AppendsOnlyNewDays()
    {
        var start = new DateOnly(2024, 1, 1);
        var day1 = new Dictionary<string, IReadOnlyList<DailyBarData>> { ["AAA"] = Series(start, 200) };

        await using var db = TestSupport.NewContext();
        var repo = new InMemoryDailyBarRepository(db);

        var handler1 = new IngestDailyBarsCommandHandler(
            new FakeUniverseService([Member("AAA")]),
            new FakeHistorySource(day1), repo, TestSupport.Options(),
            NullLogger<IngestDailyBarsCommandHandler>.Instance);
        (await handler1.Handle(new IngestDailyBarsCommand(), CancellationToken.None)).BarsAdded.Should().Be(200);

        // Second run: source now returns 201 bars (one new day). Only the new day is appended.
        var day2 = new Dictionary<string, IReadOnlyList<DailyBarData>> { ["AAA"] = Series(start, 201) };
        var handler2 = new IngestDailyBarsCommandHandler(
            new FakeUniverseService([Member("AAA")]),
            new FakeHistorySource(day2), repo, TestSupport.Options(),
            NullLogger<IngestDailyBarsCommandHandler>.Instance);

        var summary2 = await handler2.Handle(new IngestDailyBarsCommand(), CancellationToken.None);

        summary2.BarsAdded.Should().Be(1);
        (await repo.GetSinceAsync("AAA", start, CancellationToken.None)).Should().HaveCount(201);
    }

    [Fact]
    public async Task OneTickerFailure_DoesNotAbortRun_AndIsRecorded()
    {
        var start = new DateOnly(2024, 1, 1);
        var barsByTicker = new Dictionary<string, IReadOnlyList<DailyBarData>>
        {
            ["AAA"] = Series(start, 50),
            ["CCC"] = Series(start, 50),
        };

        await using var db = TestSupport.NewContext();
        var repo = new InMemoryDailyBarRepository(db);
        var handler = new IngestDailyBarsCommandHandler(
            new FakeUniverseService([Member("AAA"), Member("BBB"), Member("CCC")]),
            new FakeHistorySource(barsByTicker, throwForTicker: "BBB"),
            repo, TestSupport.Options(),
            NullLogger<IngestDailyBarsCommandHandler>.Instance);

        var summary = await handler.Handle(new IngestDailyBarsCommand(), CancellationToken.None);

        summary.Errors.Should().Be(1);
        summary.FailedTickers.Should().ContainSingle().Which.Should().Be("BBB");
        summary.BarsAdded.Should().Be(100); // AAA + CCC still ingested
    }
}
