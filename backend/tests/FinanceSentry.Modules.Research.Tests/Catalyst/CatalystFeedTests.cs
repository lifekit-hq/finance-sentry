namespace FinanceSentry.Modules.Research.Tests.Catalyst;

using System.Net;
using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Infrastructure.Sources.Catalyst;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>The two outside feeds, read through their parsers and clients with scripted HTTP; no network.</summary>
public sealed class CatalystFeedTests
{
    private static readonly DateOnly From = new(2026, 11, 13);
    private static readonly DateOnly To = new(2026, 11, 16);

    private const string NvidiaHit = """
        {"_id":"0001045450-26-000123:nvda-20261112.htm","_source":{"ciks":["0001045450"],"display_names":["NVIDIA CORP  (NVDA)  (CIK 0001045450)"],
         "adsh":"0001045450-26-000123","form":"8-K","file_date":"2026-11-13","items":["2.02","9.01"]}}
        """;

    private static string Page(int total, params string[] hits)
        => "{\"took\":3,\"hits\":{\"total\":{\"value\":" + total + ",\"relation\":\"eq\"},\"hits\":[" + string.Join(",", hits) + "]}}";

    private static EdgarFullTextSearch Search(ScriptedHttp http, CatalystOptions? options = null, AutoAdvanceClock? clock = null)
        => new(http, RequestPacer.PerSecond(5, clock ?? new AutoAdvanceClock(DateTimeOffset.UnixEpoch)), Options.Create(options ?? new CatalystOptions()));

    [Fact]
    public void ParsePage_ReadsTickerItemsDateAndDocument_FromAFullTextHit()
    {
        var (hits, total) = EdgarFullTextSearch.ParsePage(Page(1, NvidiaHit));

        total.Should().Be(1);
        var filing = hits.Should().ContainSingle().Subject.Should().ContainSingle().Subject;
        filing.Ticker.Should().Be("NVDA");
        filing.Accession.Should().Be("0001045450-26-000123");
        filing.Form.Should().Be("8-K");
        filing.Items.Should().Equal("2.02", "9.01");
        filing.FilingDate.Should().Be(new DateOnly(2026, 11, 13));
        filing.DocumentUrl.Should().Be("https://www.sec.gov/Archives/edgar/data/1045450/000104545026000123/nvda-20261112.htm");
        filing.Source.Should().Be(CatalystRules.SourceFullText);
    }

    [Fact]
    public void Tickers_ReadsEveryShareClass_AndNothingFromANameWithoutATicker()
    {
        EdgarFullTextSearch.Tickers(["Alphabet Inc.  (GOOG, GOOGL)  (CIK 0001652044)"]).Should().Equal("GOOG", "GOOGL");
        EdgarFullTextSearch.Tickers(["Berkshire Hathaway Inc  (BRK-B)  (CIK 0001067983)"]).Should().Equal("BRK-B");
        EdgarFullTextSearch.Tickers(["Some Private Fund LP  (CIK 0001234567)"]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("<html>blocked</html>")]
    [InlineData("""{"error":"x"}""")]
    [InlineData("""{"hits":{"total":{"value":0}}}""")]
    [InlineData("")]
    public void ParsePage_AShapeItDoesNotRecognise_RaisesTheFeedException(string body)
        => ((Action)(() => EdgarFullTextSearch.ParsePage(body))).Should().Throw<CatalystFeedException>();

    [Fact]
    public async Task Search_AsksOneItemAtATime_WithTheDeclaredRangeFormAndPhrase()
    {
        var http = new ScriptedHttp(_ => (HttpStatusCode.OK, Page(1, NvidiaHit)));

        var found = await Search(http).SearchAsync("2.02", From, To);

        found.Should().ContainSingle();
        var url = http.Requested.Should().ContainSingle().Subject;
        url.Should().StartWith("https://efts.sec.gov/LATEST/search-index?")
            .And.Contain("forms=8-K").And.Contain("startdt=2026-11-13").And.Contain("enddt=2026-11-16")
            .And.Contain("q=%22Item%202.02%22").And.Contain("from=0");
    }

    [Fact]
    public async Task Search_PagesToTheReportedTotal_AndRefusesToCallATruncatedReadComplete()
    {
        var options = new CatalystOptions { FullTextPageSize = 2, FullTextMaxPagesPerItem = 3 };
        var http = new ScriptedHttp(_ => (HttpStatusCode.OK, Page(100, NvidiaHit, NvidiaHit)));

        var truncated = () => Search(http, options).SearchAsync("2.02", From, To);

        await truncated.Should().ThrowAsync<CatalystFeedException>().WithMessage("*more than the 6 hits*");
        http.Requested.Should().HaveCount(3, "the page bound caps one run's request count");

        var exact = new ScriptedHttp(_ => (HttpStatusCode.OK, Page(6, NvidiaHit, NvidiaHit)));
        (await Search(exact, options).SearchAsync("2.02", From, To)).Should().HaveCount(6, "a total that fits the bound is a complete read");

        var shortPage = new ScriptedHttp(_ => (HttpStatusCode.OK, Page(3, NvidiaHit)));
        await Search(shortPage, options).SearchAsync("2.02", From, To);
        shortPage.Requested.Should().ContainSingle("a page shorter than the page size is the last one");
    }

    [Fact]
    public async Task Search_ARefusal_RaisesTheFeedException_NotAnEmptyDay()
    {
        var http = new ScriptedHttp(_ => (HttpStatusCode.Forbidden, "<html>undeclared automated tool</html>"));

        var act = () => Search(http).SearchAsync("2.02", From, To);

        await act.Should().ThrowAsync<CatalystFeedException>().WithMessage("*HTTP 403*");
    }

    [Fact]
    public async Task Search_SpacesRequestsToHalfTheSecCeiling()
    {
        var options = new CatalystOptions { FullTextPageSize = 1, FullTextMaxPagesPerItem = 6 };
        var clock = new AutoAdvanceClock(DateTimeOffset.UnixEpoch);
        var http = new ScriptedHttp(_ => (HttpStatusCode.OK, Page(6, NvidiaHit)));

        await Search(http, options, clock).SearchAsync("2.02", From, To);

        http.Requested.Should().HaveCount(6);
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(5 * 200), "5 requests a second leaves 200 ms between six requests");
    }

    [Fact]
    public void GdeltParse_ReadsDailyCountsAndTheNormaliser()
    {
        const string body = """
            {"query_details":{"title":"\"HBM memory\""},"timeline":[{"series":"Article Count","data":[
             {"date":"20261111T000000Z","value":40,"norm":90000},{"date":"20261112T000000Z","value":55.0,"norm":91000}]}]}
            """;

        var days = GdeltTimelineClient.Parse(body, "HBM memory");

        days.Should().Equal(
            new GdeltDay(new DateOnly(2026, 11, 11), 40, 90000),
            new GdeltDay(new DateOnly(2026, 11, 12), 55, 91000));
    }

    [Fact]
    public void GdeltParse_SumsSubDailyBucketsIntoOneCountPerUtcDay()
    {
        const string body = """
            {"timeline":[{"series":"Article Count","data":[
             {"date":"20261111T000000Z","value":10,"norm":1000},{"date":"20261111T120000Z","value":15,"norm":1500},
             {"date":"20261112T000000Z","value":5,"norm":900}]}]}
            """;

        GdeltTimelineClient.Parse(body, "HBM memory").Should().Equal(
            new GdeltDay(new DateOnly(2026, 11, 11), 25, 2500),
            new GdeltDay(new DateOnly(2026, 11, 12), 5, 900));
    }

    [Fact]
    public void GdeltParse_NoTimelineIsZeroCoverage_ButTextAndEmptyBodiesAreFailures()
    {
        GdeltTimelineClient.Parse("{}", "obscure phrase").Should().BeEmpty();
        ((Action)(() => GdeltTimelineClient.Parse("Please limit requests to one every 5 seconds", "x"))).Should().Throw<CatalystFeedException>();
        ((Action)(() => GdeltTimelineClient.Parse("", "x"))).Should().Throw<CatalystFeedException>();
    }

    [Fact]
    public async Task GdeltClient_AsksForRawCountsOfTheQuotedPhrase_AndNeverReadsAThrottleAsZero()
    {
        var http = new ScriptedHttp(url => url.Contains("throttled")
            ? (HttpStatusCode.TooManyRequests, "Please limit requests to one every 5 seconds")
            : (HttpStatusCode.OK, """{"timeline":[{"series":"Article Count","data":[{"date":"20261112T000000Z","value":7}]}]}"""));
        var client = new GdeltTimelineClient(http, new RequestPacer(TimeSpan.FromSeconds(5), new AutoAdvanceClock(DateTimeOffset.UnixEpoch)), Options.Create(new CatalystOptions()));

        var days = await client.GetDailyVolumeAsync("HBM memory", 14);

        days.Should().ContainSingle().Which.Articles.Should().Be(7);
        http.Requested[0].Should().Contain("mode=timelinevolraw").And.Contain("timespan=14d").And.Contain("query=%22HBM%20memory%22%20sourcelang%3Aenglish");

        var throttled = () => client.GetDailyVolumeAsync("throttled", 14);
        await throttled.Should().ThrowAsync<CatalystFeedException>().WithMessage("*HTTP 429*");
    }

    [Fact]
    public async Task GdeltClient_SpacesRequestsFiveSecondsApart()
    {
        var clock = new AutoAdvanceClock(DateTimeOffset.UnixEpoch);
        var http = new ScriptedHttp(_ => (HttpStatusCode.OK, "{}"));
        var client = new GdeltTimelineClient(http, new RequestPacer(TimeSpan.FromSeconds(5), clock), Options.Create(new CatalystOptions()));

        for (var i = 0; i < 4; i++)
        {
            await client.GetDailyVolumeAsync($"phrase {i}", 7);
        }

        clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(15), "four requests leave three 5-second gaps");
    }
}
