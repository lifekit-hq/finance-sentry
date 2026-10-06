namespace FinanceSentry.Modules.Research.Tests.Companion;

using System.Text.RegularExpressions;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Xunit;

/// <summary>
/// External-contract test (constitution-mandated) for the TrendForce press-center article-list
/// structure (feature 030, T038). Structural drift MUST throw <see cref="NewsSourceParseException"/>.
/// <para>
/// Two kinds of fixture live here. The <c>Companion/Fixtures/*.html</c> files are verbatim live
/// captures — the evidence trail for issue #318, where the source had failed 17 times running because
/// feature 030 seeded it at <c>/presscenter/</c> (the marketing hub) instead of <c>/presscenter/news</c>,
/// and the hand-written fixtures below never noticed because they were invented rather than captured.
/// The inline consts stay for synthetic drift cases: they document shapes we want to survive, not
/// shapes the site currently serves.
/// </para>
/// </summary>
public sealed class TrendForcePageContractTests
{
    private const string PageUrl = "https://www.trendforce.com/presscenter/news";

    private const string HubUrl = "https://www.trendforce.com/presscenter/";

    /// <summary>The permalink shape every emitted article URL must have: no charts, videos or promos.</summary>
    private static readonly Regex Permalink =
        new(@"^https://www\.trendforce\.com/presscenter/news/\d{6,}-\d+\.html$", RegexOptions.IgnoreCase);

    private static Task<string> LoadFixtureAsync(string name)
        => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Companion", "Fixtures", name));

    private const string Fixture = """
    <html><body>
      <div class="press-list">
        <article>
          <a href="/presscenter/news/20260721-12345.html"><h3 class="title">DRAM prices rise on HBM demand</h3></a>
          <time datetime="2026-07-21T00:00:00Z">Jul 21, 2026</time>
          <p class="summary">Contract DRAM prices climbed as HBM allocation tightened supply.</p>
        </article>
        <article>
          <a href="https://www.trendforce.com/presscenter/news/20260720-99999.html"><h3 class="title">NAND flash quarterly update</h3></a>
          <time datetime="2026-07-20">Jul 20, 2026</time>
        </article>
      </div>
    </body></html>
    """;

    private const string CurrentShapeFixture = """
    <html><body>
      <div class="navbar navbar-inner press-news-list">
        <div class="list-items text-left">
          <div class="list-item">
            <div class="row">
              <div class="col-md-12 margin-t-20 padding-l-r-15">
                <h3 class="font-size-18"><a class="title-link" href="/presscenter/news/20260709-13140.html"><strong>Long-Term Agreements Cap Price Increases; Server DRAM Contract Prices Expected to Rise 13-18% QoQ in 3Q26, Says TrendForce</strong></a></h3>
                <h4 class="font-size-16 color-green margin-t-10"><strong>9 July 2026</strong></h4>
                <p class="font-size-16">TrendForce's latest memory pricing survey indicates expected price hikes.</p>
              </div>
            </div>
          </div>
        </div>
      </div>
      <ul class="list"><li><a href="/presscenter/news/Semiconductors">Semiconductors</a></li></ul>
    </body></html>
    """;

    [Fact]
    public async Task Parse_extracts_title_url_and_date_from_article_list()
    {
        var articles = await TrendForcePageSource.ParseAsync(Fixture, PageUrl);

        articles.Should().HaveCount(2);

        var first = articles[0];
        first.Title.Should().Be("DRAM prices rise on HBM demand");
        first.Url.Should().Be("https://www.trendforce.com/presscenter/news/20260721-12345.html",
            "a relative href must resolve against the page URL");
        first.PublishedAt.Should().Be(DateTimeOffset.Parse("2026-07-21T00:00:00Z"));
        first.Summary.Should().Contain("HBM allocation");

        articles[1].Title.Should().Be("NAND flash quarterly update");
        articles[1].Url.Should().Be("https://www.trendforce.com/presscenter/news/20260720-99999.html");
    }

    // Current live markup (2026-08): the press list restyled from .list-item to
    // .advs-box.niche-box-post, with the date rendered inside <p class="bd-month">.
    private const string AdvsBoxFixture = """
    <html><body>
      <div class="press-list-wrapper">
        <div class="advs-box advs-box-top-icon-img niche-box-post boxed-inverse">
          <div class="block-infos"><div class="block-data"><p class="bd-day"></p><p class="bd-month">4 August 2026</p></div></div>
          <a class="img-box" href="/presscenter/news/20260804-13166.html"><img src="/images/news-01.jpg" alt=""></a>
          <div class="advs-box-content">
            <h2><a class="text-m text-ellipsis-2" href="/presscenter/news/20260804-13166.html"><strong>DRAM Supply to Remain Tight in 2027, Prompting NVIDIA to Lower HBM Configurations</strong></a></h2>
            <p class="text-m">Memory supply stays constrained through 2027 as HBM absorbs wafer capacity.</p>
          </div>
        </div>
      </div>
    </body></html>
    """;

    // Classes we've never seen, but the /presscenter/news/<date>-<id>.html permalink is unchanged. The
    // parser must recover the article from the href pattern rather than throwing on class drift.
    private const string DriftedClassesFixture = """
    <html><body>
      <section class="brand-new-layout-v3">
        <div class="whatever-card">
          <h3 class="totally-renamed"><a href="/presscenter/news/20260801-10001.html">Server DRAM contract prices climb 15% QoQ</a></h3>
          <span class="posted">1 August 2026</span>
        </div>
      </section>
      <nav><a href="/presscenter/news/Semiconductors">Semiconductors</a></nav>
    </body></html>
    """;

    [Fact]
    public async Task Parse_extracts_current_advs_box_shape()
    {
        var articles = await TrendForcePageSource.ParseAsync(AdvsBoxFixture, PageUrl);

        articles.Should().ContainSingle("the hero/list duplicate for one permalink must collapse by URL");
        articles[0].Title.Should().Contain("DRAM Supply to Remain Tight");
        articles[0].Url.Should().Be("https://www.trendforce.com/presscenter/news/20260804-13166.html");
        articles[0].PublishedAt.Should().Be(new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero));
        articles[0].Summary.Should().Contain("HBM absorbs wafer capacity");
    }

    [Fact]
    public async Task Parse_recovers_articles_from_href_pattern_when_classes_drift()
    {
        var articles = await TrendForcePageSource.ParseAsync(DriftedClassesFixture, PageUrl);

        articles.Should().ContainSingle("only the permalink matches — the category nav link must be ignored");
        articles[0].Title.Should().Contain("Server DRAM contract prices");
        articles[0].Url.Should().Be("https://www.trendforce.com/presscenter/news/20260801-10001.html");
    }

    [Fact]
    public async Task Parse_throws_when_article_list_is_missing()
    {
        const string drifted = "<html><body><div>no articles here</div></body></html>";

        var act = async () => await TrendForcePageSource.ParseAsync(drifted, PageUrl);

        await act.Should().ThrowAsync<NewsSourceParseException>();
    }

    [Fact]
    public async Task Parse_extracts_current_press_news_list_shape()
    {
        var articles = await TrendForcePageSource.ParseAsync(CurrentShapeFixture, PageUrl);

        articles.Should().ContainSingle();
        articles[0].Title.Should().Contain("Server DRAM Contract Prices");
        articles[0].Url.Should().Be("https://www.trendforce.com/presscenter/news/20260709-13140.html");
        articles[0].PublishedAt.Should().Be(new DateTimeOffset(2026, 7, 9, 0, 0, 0, TimeSpan.Zero));
        articles[0].Summary.Should().Contain("memory pricing");
    }

    [Fact]
    public async Task Live_capture_of_the_news_list_parses_to_dated_articles()
    {
        var html = await LoadFixtureAsync("trendforce-presscenter-news-2026-09-02.html");

        var articles = await TrendForcePageSource.ParseAsync(html, PageUrl);

        articles.Should().NotBeEmpty("the canonical press list is what the source is seeded to read");
        articles.Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.Title));
        articles.Should().OnlyContain(a => Permalink.IsMatch(a.Url));
        articles.Should().OnlyContain(a => a.PublishedAt.Year >= 2026, "the list carries only recent releases");
        articles.Select(a => a.Url).Should().OnlyHaveUniqueItems();
        articles.Should().Contain(
            a => a.Url == "https://www.trendforce.com/presscenter/news/20260825-13198.html" &&
                 a.Title.Contains("DRAM and NAND Flash") &&
                 a.PublishedAt == new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero),
            "a DRAM release from the capture must survive title/url/date extraction intact");
    }

    [Fact]
    public async Task Live_capture_of_the_hub_yields_only_press_release_permalinks()
    {
        var html = await LoadFixtureAsync("trendforce-presscenter-hub-2026-09-02.html");

        var articles = await TrendForcePageSource.ParseAsync(html, HubUrl);

        articles.Should().NotBeEmpty();
        // Before issue #318 this page contributed a dramexchange.com spot-price promo, a 2023
        // /presscenter/chart/ entry and two 2019-2020 /presscenter/video/ teasers to the DRAM feed.
        articles.Should().OnlyContain(a => Permalink.IsMatch(a.Url));
        articles.Should().NotContain(a => a.Url.Contains("dramexchange.com"));
        articles.Should().NotContain(a => a.Url.Contains("/presscenter/video/"));
        articles.Should().NotContain(a => a.Url.Contains("/presscenter/chart/"));
    }

    [Fact]
    public async Task Parse_throws_when_cards_exist_but_carry_no_article_permalinks()
    {
        // A page of cards that are all promos: recognisable as cards, none an article. Returning an
        // empty list here would turn a drift signal into a silent coverage gap — the #318 failure mode.
        const string promosOnly = """
        <html><body>
          <div class="advs-box niche-box-post">
            <h2><a href="/presscenter/video/20200214-38.html">Top 10 Trends</a></h2>
            <p class="bd-month">14 February 2020</p>
          </div>
          <div class="advs-box niche-box-post">
            <h2><a href="http://www.dramexchange.com/market/daily.aspx">Daily Express Spot Market</a></h2>
            <p class="bd-month">2 September 2026</p>
          </div>
        </body></html>
        """;

        var act = async () => await TrendForcePageSource.ParseAsync(promosOnly, PageUrl);

        await act.Should().ThrowAsync<NewsSourceParseException>();
    }

    [Fact(Skip = "Live network smoke test — run manually to confirm TrendForce markup still parses.")]
    public async Task Live_trendforce_page_still_parses()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        var html = await http.GetStringAsync(PageUrl);

        var articles = await TrendForcePageSource.ParseAsync(html, PageUrl);

        articles.Should().NotBeEmpty();
    }
}
