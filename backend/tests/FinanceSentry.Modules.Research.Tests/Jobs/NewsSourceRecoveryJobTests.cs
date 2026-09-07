namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FinanceSentry.Modules.Research.Tests.Companion;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// <see cref="NewsSourceRecoveryJob"/> (spec 047). Auto-retirement used to be permanent — the ingestion
/// sweep only walks enabled sources and nothing re-enabled a row — so the TrendForce source stayed dark
/// for six weeks after the parser fix that would have healed it (issue #318).
/// </summary>
public sealed class NewsSourceRecoveryJobTests
{
    private const string PageUrl = "https://www.trendforce.com/presscenter/news";

    private readonly FakeNewsSourceRepository _sources = new();
    private readonly FakeNewsRepository _news = new();

    [Fact]
    public async Task A_retired_source_that_fetches_again_is_returned_to_service()
    {
        _sources.Sources.Add(RetiredSource());

        await JobWith(new FakeNewsPageSource(PageUrl, [Candidate("HBM contract prices rise")])).ExecuteAsync();

        var recovered = _sources.Sources.Single();
        recovered.Enabled.Should().BeTrue();
        recovered.ConsecutiveFailures.Should().Be(0);
        recovered.LastFailureReason.Should().BeNull();
        recovered.LastSuccessAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_successful_probe_persists_the_articles_it_fetched_with_thesis_tags()
    {
        var thesisId = Guid.NewGuid();
        var source = RetiredSource();
        source.ThesisId = thesisId;
        source.Keywords = ["DRAM", "HBM"];
        _sources.Sources.Add(source);

        await JobWith(new FakeNewsPageSource(
            PageUrl, [Candidate("HBM contract prices rise"), Candidate("Retail sales dip")])).ExecuteAsync();

        _news.Inserted.Should().HaveCount(2, "a probe is a real ingestion, not a ping");
        _news.Inserted.Single(a => a.Title == "HBM contract prices rise").ThesisIds.Should().Equal(thesisId);
        _news.Inserted.Single(a => a.Title == "Retail sales dip").ThesisIds.Should().BeEmpty();
    }

    [Fact]
    public async Task A_probe_that_still_fails_leaves_the_source_retired_without_inflating_its_counter()
    {
        _sources.Sources.Add(RetiredSource());

        await JobWith(new FakeNewsPageSource(
            PageUrl, failure: new NewsSourceParseException("article list not found"))).ExecuteAsync();

        var stillDark = _sources.Sources.Single();
        stillDark.Enabled.Should().BeFalse();
        stillDark.ConsecutiveFailures.Should().Be(17, "the counter tracks failures while in service");
        stillDark.LastSuccessAt.Should().BeNull();
    }

    [Fact]
    public async Task A_failed_probe_refreshes_the_recorded_reason_so_diagnosis_starts_from_todays_error()
    {
        _sources.Sources.Add(RetiredSource());

        await JobWith(new FakeNewsPageSource(
            PageUrl, failure: new HttpRequestException("503 from origin"))).ExecuteAsync();

        _sources.Sources.Single().LastFailureReason.Should().Be("503 from origin");
    }

    [Fact]
    public async Task One_source_that_throws_does_not_stop_the_next_from_recovering()
    {
        const string OtherUrl = "https://www.example.com/newsroom";
        var broken = RetiredSource();
        broken.Name = "Broken Source";
        broken.Url = OtherUrl;
        _sources.Sources.Add(broken);
        _sources.Sources.Add(RetiredSource());

        await JobWith(
            new FakeNewsPageSource(OtherUrl, failure: new NewsSourceParseException("markup drift")),
            new FakeNewsPageSource(PageUrl, [Candidate("HBM contract prices rise")])).ExecuteAsync();

        _sources.Sources.Single(s => s.Url == OtherUrl).Enabled.Should().BeFalse();
        _sources.Sources.Single(s => s.Url == PageUrl).Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Enabled_sources_are_left_to_the_ingestion_sweep()
    {
        var healthy = RetiredSource();
        healthy.Enabled = true;
        healthy.ConsecutiveFailures = 0;
        _sources.Sources.Add(healthy);
        var page = new FakeNewsPageSource(PageUrl, [Candidate("HBM contract prices rise")]);

        await JobWith(page).ExecuteAsync();

        page.FetchCount.Should().Be(0);
        _news.Inserted.Should().BeEmpty();
    }

    private NewsSourceRecoveryJob JobWith(params INewsPageSource[] pageSources)
        => new(
            _sources,
            _news,
            new NewsSourceFetcher(new FakeMarketNewsService(), pageSources),
            NullLogger<NewsSourceRecoveryJob>.Instance);

    private static NewsSource RetiredSource() => new()
    {
        Name = "TrendForce Press Center",
        Kind = NewsSourceKind.Page,
        Url = PageUrl,
        Enabled = false,
        ConsecutiveFailures = 17,
        LastFailureReason = "article list not found",
    };

    private static NewsPageArticle Candidate(string title)
        => new(title, $"{PageUrl}/{Uri.EscapeDataString(title)}.html", DateTimeOffset.UtcNow, null);
}
