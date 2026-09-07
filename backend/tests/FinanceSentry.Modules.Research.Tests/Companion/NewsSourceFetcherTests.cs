namespace FinanceSentry.Modules.Research.Tests.Companion;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Xunit;

/// <summary>
/// <see cref="NewsSourceFetcher"/> is the one definition of "fetch a registered source" shared by the
/// 30-minute ingestion sweep and the recovery probe (spec 047), so the mapping it applies — adapter
/// dispatch, thesis tagging, column-width trimming — is asserted here once for both callers.
/// </summary>
public sealed class NewsSourceFetcherTests
{
    private const string PageUrl = "https://www.trendforce.com/presscenter/news";

    [Fact]
    public async Task Page_source_candidates_are_stamped_with_the_source_label_and_a_content_hash()
    {
        var fetcher = new NewsSourceFetcher(
            new FakeMarketNewsService(),
            [new FakeNewsPageSource(PageUrl, [Candidate("HBM demand climbs")])]);

        var articles = await fetcher.FetchAsync(PageSource());

        articles.Should().ContainSingle();
        articles[0].Source.Should().Be("src:TrendForce Press Center");
        articles[0].Title.Should().Be("HBM demand climbs");
        articles[0].ContentHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Rss_sources_go_through_the_feed_parser_rather_than_a_page_adapter()
    {
        var page = new FakeNewsPageSource(PageUrl, [Candidate("never fetched")]);
        var feed = new FakeMarketNewsService([new NewsArticle { Title = "Fed holds", Url = "https://feed/1" }]);
        var fetcher = new NewsSourceFetcher(feed, [page]);

        var articles = await fetcher.FetchAsync(new NewsSource
        {
            Name = "Yahoo Finance Top Stories",
            Kind = NewsSourceKind.Rss,
            Url = "https://finance.yahoo.com/news/rssindex",
        });

        articles.Should().ContainSingle().Which.Title.Should().Be("Fed holds");
        feed.RequestedLabel.Should().Be("src:Yahoo Finance Top Stories");
        page.FetchCount.Should().Be(0);
    }

    [Fact]
    public async Task A_page_url_no_adapter_claims_throws_rather_than_returning_nothing()
    {
        var fetcher = new NewsSourceFetcher(
            new FakeMarketNewsService(),
            [new FakeNewsPageSource("https://elsewhere.example/news")]);

        var fetch = async () => await fetcher.FetchAsync(PageSource());

        await fetch.Should().ThrowAsync<NewsSourceParseException>()
            .WithMessage($"*{PageUrl}*");
    }

    [Fact]
    public async Task Articles_are_tagged_with_the_source_thesis_only_when_a_keyword_matches()
    {
        var thesisId = Guid.NewGuid();
        var fetcher = new NewsSourceFetcher(
            new FakeMarketNewsService(),
            [new FakeNewsPageSource(PageUrl, [Candidate("HBM demand climbs"), Candidate("Retail sales dip")])]);

        var source = PageSource();
        source.ThesisId = thesisId;
        source.Keywords = ["DRAM", "HBM"];

        var articles = await fetcher.FetchAsync(source);

        articles.Single(a => a.Title == "HBM demand climbs").ThesisIds.Should().Equal(thesisId);
        articles.Single(a => a.Title == "Retail sales dip").ThesisIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Over_long_fields_are_trimmed_to_the_column_widths()
    {
        var candidate = new NewsPageArticle(
            new string('t', 600), $"{PageUrl}/{new string('u', 2100)}", DateTimeOffset.UtcNow, new string('s', 4200));
        var fetcher = new NewsSourceFetcher(
            new FakeMarketNewsService(), [new FakeNewsPageSource(PageUrl, [candidate])]);

        var articles = await fetcher.FetchAsync(PageSource());

        articles[0].Title.Should().HaveLength(500);
        articles[0].Url.Should().HaveLength(2000);
        articles[0].Summary.Should().HaveLength(4000);
    }

    private static NewsSource PageSource() => new()
    {
        Name = "TrendForce Press Center",
        Kind = NewsSourceKind.Page,
        Url = PageUrl,
    };

    private static NewsPageArticle Candidate(string title)
        => new(title, $"{PageUrl}/{Uri.EscapeDataString(title)}.html", DateTimeOffset.UtcNow, null);
}
