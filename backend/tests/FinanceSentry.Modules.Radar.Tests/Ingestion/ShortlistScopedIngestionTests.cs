namespace FinanceSentry.Modules.Radar.Tests.Ingestion;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Commands;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Stage 2 of the #558 funnel is only as cheap as the universe it runs over: an ingestion cycle may
/// fetch bars for the book, the seed lenses and the stage-1 shortlist — and for nothing else. The
/// index behind the shortlist is 200 names here; a run that fetched them would show up as a fan-out.
/// </summary>
public sealed class ShortlistScopedIngestionTests
{
    private const string Held = "HELD";
    private const string Watched = "WTCH";
    private const string Benchmark = "SPY";
    private const string Sector = "XLK";

    private static readonly string[] Index =
        [.. Enumerable.Range(0, 200).Select(i => FormattableString.Invariant($"IDX{i:D3}"))];

    private static readonly Guid BookOwner = Guid.NewGuid();

    [Fact]
    public async Task ACycleFetchesTheBookAndTheShortlistOnly()
    {
        await using var harness = Harness.Create(broadEnabled: true, shortlist: ["IDX007", "IDX042", "IDX199"]);

        await harness.IngestAsync();

        harness.Requested.Should().BeEquivalentTo(
            new[] { Benchmark, Sector, Held, Watched, "IDX007", "IDX042", "IDX199" },
            "the cycle costs one fetch per book member, lens and shortlisted name — never per constituent");
        harness.Requested.Should().NotContain("IDX000", "a constituent stage 1 did not shortlist is never ingested");
    }

    [Fact]
    public async Task TheBookIsFetchedBeforeTheShortlist()
    {
        await using var harness = Harness.Create(broadEnabled: true, shortlist: ["AAAA", "IDX042"]);

        await harness.IngestAsync();

        harness.Requested.Take(4).Should().BeEquivalentTo(
            new[] { Benchmark, Sector, Held, Watched },
            "an upstream rate limit must cost breadth, never the freshness of the book");
        harness.Requested.Should().EndWith(
            ["AAAA", "IDX042"], "shortlisted names follow, alphabetical order notwithstanding");
    }

    [Fact]
    public async Task WithTheFlagOff_TheCycleStaysAtTheBook()
    {
        await using var harness = Harness.Create(broadEnabled: false, shortlist: ["IDX007"]);

        await harness.IngestAsync();

        harness.Requested.Should().BeEquivalentTo(new[] { Benchmark, Sector, Held, Watched });
        harness.ShortlistReads.Should().Be(0, "a disabled funnel must not pay stage 1's upstream cost either");
    }

    [Fact]
    public async Task AnEmptyShortlistDegradesToTheBookRatherThanFailingTheCycle()
    {
        await using var harness = Harness.Create(broadEnabled: true, shortlist: []);

        var summary = await harness.IngestAsync();

        summary.Errors.Should().Be(0);
        harness.Requested.Should().BeEquivalentTo(new[] { Benchmark, Sector, Held, Watched });
    }

    private sealed class Harness(
        RadarDbContext db,
        FakeHistorySource history,
        FakeShortlistSource shortlist,
        IngestDailyBarsCommandHandler handler) : IAsyncDisposable
    {
        public List<string> Requested => history.Requested;

        public int ShortlistReads => shortlist.Reads;

        public static Harness Create(bool broadEnabled, string[] shortlist)
        {
            var db = TestSupport.NewContext();
            var options = TestSupport.Options(new RadarOptions
            {
                Benchmark = Benchmark,
                BenchmarkTickers = [Benchmark],
                SectorTickers = [Sector],
                IndustryTickers = [],
                BroadUniverseEnabled = broadEnabled,
            });

            var banking = new Mock<IBankingTotalsReader>();
            banking.Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([BookOwner]);
            var brokerage = new Mock<IBrokerageHoldingsReader>();
            brokerage.Setup(b => b.GetHoldingsAsync(BookOwner, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new BrokerageHoldingSummary(Held, "STK", 1m, 100m, DateTime.UtcNow, "IBKR")]);
            var watchlist = new Mock<IWatchlistReader>();
            watchlist.Setup(w => w.ListTickersAsync(BookOwner, It.IsAny<CancellationToken>()))
                .ReturnsAsync([Watched]);

            var history = new FakeHistorySource(
                Index.Concat([Benchmark, Sector, Held, Watched, "AAAA"])
                    .ToDictionary(t => t, _ => (IReadOnlyList<DailyBarData>)[]));
            var source = new FakeShortlistSource { Tickers = shortlist };

            var universeService = new RadarUniverseService(
                new RadarUniverseRepository(db), brokerage.Object, watchlist.Object, banking.Object, options, source);

            var handler = new IngestDailyBarsCommandHandler(
                universeService,
                history,
                new DailyBarRepository(db),
                options,
                NullLogger<IngestDailyBarsCommandHandler>.Instance);

            return new Harness(db, history, source, handler);
        }

        public Task<IngestRunSummary> IngestAsync()
            => handler.Handle(new IngestDailyBarsCommand(), CancellationToken.None);

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
