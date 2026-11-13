namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FinanceSentry.Modules.Research.Tests.Coverage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// The news ingestion budget: the book (watchlist, then thesis, then held names) keeps its 60 places, and coverage names
/// the book does not name are read in rotating slices on top, so a coverage list of 100 is covered without starving a held name.
/// </summary>
public sealed class NewsIngestionCoverageTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly CoverageHarness _h = new();
    private readonly MovableClock _clock = new(Start);
    private readonly Mock<IMarketNewsService> _news = new();
    private readonly Mock<ICryptoHoldingsReader> _crypto = new();
    private readonly List<string[]> _fetched = [];

    public NewsIngestionCoverageTests()
    {
        _crypto.Setup(c => c.GetHoldingsAsync(It.IsAny<Guid>(), default)).ReturnsAsync([]);
        _news.Setup(n => n.IngestForTickersAsync(It.IsAny<IReadOnlyCollection<string>>(), default))
            .Callback<IReadOnlyCollection<string>, CancellationToken>((tickers, _) => _fetched.Add([.. tickers]))
            .ReturnsAsync(0);
    }

    private NewsIngestionJob Job(CoverageOptions? options = null)
    {
        var sources = new Mock<INewsSourceRepository>();
        sources.Setup(s => s.ListEnabledAsync(default)).ReturnsAsync([]);
        return new NewsIngestionJob(
            _h.Db, _h.Brokerage.Object, _crypto.Object, _news.Object, sources.Object, new Mock<INewsRepository>().Object,
            new NewsSourceFetcher(_news.Object, []), new Mock<IAlertGeneratorService>().Object, _h.Banking.Object,
            new LookaheadUniverse(_h.Brokerage.Object, _h.Watchlist.Object, new ThesisRepository(_h.Db), _crypto.Object, _h.Coverage),
            Microsoft.Extensions.Options.Options.Create(options ?? _h.Options), _clock, NullLogger<NewsIngestionJob>.Instance);
    }

    private async Task CoverAsync(params string[] tickers)
    {
        foreach (var ticker in tickers)
        {
            _h.Db.CoverageMembers.Add(new CoverageMember { UserId = CoverageHarness.UserId, Ticker = ticker, AddedReason = CoverageReason.Screen });
        }

        await _h.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Without_coverage_the_run_reads_watchlist_then_thesis_then_held_names()
    {
        _h.Hold("NVDA");
        await _h.ThesisAsync("MU");
        _h.Db.WatchlistItems.Add(new WatchlistItem { UserId = CoverageHarness.UserId, Ticker = "AAPL" });
        await _h.Db.SaveChangesAsync();
        _h.WatchedTickers.Add("AAPL");

        await Job().IngestTickersAsync();

        _fetched.Should().ContainSingle().Which.Should().Equal("AAPL", "MU", "NVDA");
    }

    [Fact]
    public async Task When_the_book_exceeds_the_budget_the_tail_is_cut_in_watchlist_thesis_held_order()
    {
        for (var i = 0; i < 30; i++)
        {
            _h.Db.WatchlistItems.Add(new WatchlistItem { UserId = CoverageHarness.UserId, Ticker = $"W{i:00}" });
            _h.WatchedTickers.Add($"W{i:00}");
        }

        await _h.Db.SaveChangesAsync();
        for (var i = 0; i < 20; i++)
        {
            await _h.ThesisAsync($"T{i:00}");
        }

        for (var i = 0; i < 20; i++)
        {
            _h.Hold($"H{i:00}");
        }

        await Job().IngestTickersAsync();

        var fetched = _fetched.Should().ContainSingle().Subject;
        fetched.Should().HaveCount(60);
        fetched.Take(30).Should().OnlyContain(t => t.StartsWith('W'));
        fetched.Skip(30).Take(20).Should().OnlyContain(t => t.StartsWith('T'));
        fetched.Skip(50).Should().Equal(Enumerable.Range(0, 10).Select(i => $"H{i:00}"));
    }

    [Fact]
    public async Task Coverage_names_ride_on_top_of_the_book_in_rotating_slices_that_cover_the_whole_list()
    {
        _h.Hold("NVDA");
        var coverage = new[] { "NVDA" }.Concat(Enumerable.Range(0, 99).Select(i => $"C{i:00}")).ToArray();
        await CoverAsync(coverage);

        var job = Job();
        for (var run = 0; run < 3; run++)
        {
            _clock.Now = Start.AddMinutes(30 * run);
            await job.IngestTickersAsync();
        }

        _fetched.Should().HaveCount(3);
        _fetched.Should().OnlyContain(f => f.Contains("NVDA") && f.Length <= 1 + _h.Options.NewsCoverageTickersPerRun);
        _fetched.SelectMany(f => f).Distinct().Should().BeEquivalentTo(coverage, "every covered name is read within one rotation");
        _fetched.SelectMany(f => f.Where(t => t != "NVDA")).Should().OnlyHaveUniqueItems("no coverage name is read twice in a rotation");
    }

    [Fact]
    public async Task A_slice_of_coverage_does_not_take_a_held_names_place_when_the_book_is_full()
    {
        for (var i = 0; i < 60; i++)
        {
            _h.Hold($"H{i:00}");
        }

        await CoverAsync("C00", "C01");

        await Job().IngestTickersAsync();

        var fetched = _fetched.Should().ContainSingle().Subject;
        fetched.Take(60).Should().OnlyContain(t => t.StartsWith('H'));
        fetched.Skip(60).Should().BeEquivalentTo(["C00", "C01"]);
    }

    [Fact]
    public async Task A_coverage_budget_of_zero_reads_no_coverage_only_name()
    {
        _h.Hold("NVDA");
        await CoverAsync("NVDA", "C00");

        await Job(new CoverageOptions { NewsCoverageTickersPerRun = 0 }).IngestTickersAsync();

        _fetched.Should().ContainSingle().Which.Should().Equal("NVDA");
    }
}
