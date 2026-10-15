namespace FinanceSentry.Tests.Unit.Fx;

using System.Net;
using System.Text;
using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class HistoricalExchangeRateTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri;
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (NbuHistoricalExchangeRateProvider Provider, StubHandler Handler) BuildNbu(
        string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://bank.gov.ua/") };
        return (new NbuHistoricalExchangeRateProvider(
            http, NullLogger<NbuHistoricalExchangeRateProvider>.Instance), handler);
    }

    private static FrankfurterHistoricalExchangeRateProvider BuildFrankfurter(
        string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var http = new HttpClient(new StubHandler(status, body))
        {
            BaseAddress = new Uri("https://api.frankfurter.dev/"),
        };
        return new FrankfurterHistoricalExchangeRateProvider(
            http, NullLogger<FrankfurterHistoricalExchangeRateProvider>.Instance);
    }

    // Shape copied from a live bank.gov.ua response.
    private const string NbuBody =
        """
        [
          {"exchangedate":"01.01.2024","r030":840,"cc":"USD","rate":38.002,"units":1},
          {"exchangedate":"02.01.2024","r030":840,"cc":"USD","rate":38.0144,"units":1}
        ]
        """;

    [Fact]
    public async Task Nbu_InvertsUahPerUsdIntoUsdPerUnit()
    {
        var (provider, _) = BuildNbu(NbuBody);

        var series = await provider.GetUsdPerUnitSeriesAsync(
            "UAH", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2));

        // 1 / 38.002 — a hryvnia is worth about 2.6 US cents.
        series[new DateOnly(2024, 1, 1)].Should().BeApproximately(0.026314m, 0.000001m);
        series[new DateOnly(2024, 1, 2)].Should().BeApproximately(0.026306m, 0.000001m);
    }

    [Fact]
    public async Task Nbu_RequestsTheDateRangeInTheFormatTheFeedExpects()
    {
        var (provider, handler) = BuildNbu(NbuBody);

        await provider.GetUsdPerUnitSeriesAsync("UAH", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2));

        handler.LastRequestUri!.Query.Should().Contain("start=20240101").And.Contain("end=20240102");
    }

    [Fact]
    public async Task Nbu_OnlySupportsUah_SinceItQuotesEverythingAgainstTheHryvnia()
    {
        var (provider, _) = BuildNbu(NbuBody);

        provider.Supports("UAH").Should().BeTrue();
        provider.Supports("uah").Should().BeTrue();
        provider.Supports("EUR").Should().BeFalse();
        (await provider.GetUsdPerUnitSeriesAsync("EUR", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Nbu_ReturnsEmpty_WhenFeedIsUnreachable()
    {
        var (provider, _) = BuildNbu("nonsense", HttpStatusCode.InternalServerError);

        var series = await provider.GetUsdPerUnitSeriesAsync(
            "UAH", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2));

        series.Should().BeEmpty();
    }

    [Fact]
    public async Task Frankfurter_ReadsUsdQuotesDirectly_WithoutInverting()
    {
        const string body =
            """{"amount":1.0,"base":"EUR","rates":{"2024-01-02":{"USD":1.0956},"2024-01-03":{"USD":1.0919}}}""";
        var provider = BuildFrankfurter(body);

        var series = await provider.GetUsdPerUnitSeriesAsync(
            "EUR", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3));

        series[new DateOnly(2024, 1, 2)].Should().Be(1.0956m);
        series[new DateOnly(2024, 1, 3)].Should().Be(1.0919m);
    }

    [Fact]
    public void Frankfurter_DoesNotClaimUah_WhichIsWhyNbuExists()
    {
        var provider = BuildFrankfurter("{}");

        provider.Supports("UAH").Should().BeFalse();
        provider.Supports("EUR").Should().BeTrue();
    }

    private static CachingHistoricalExchangeRateService BuildService(
        params IHistoricalExchangeRateProvider[] providers) =>
        new(providers,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<CachingHistoricalExchangeRateService>.Instance);

    [Fact]
    public async Task Service_CarriesTheLastPublishedRateAcrossUnpublishedDays()
    {
        // NBU publishes 1 Jan and 2 Jan; 3–4 Jan are a weekend with no quote.
        var (nbu, _) = BuildNbu(NbuBody);
        var service = BuildService(nbu);

        var series = await service.GetDailySeriesAsync(
            "UAH", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 4));

        series.Should().HaveCount(4);
        series[new DateOnly(2024, 1, 3)].Should().Be(series[new DateOnly(2024, 1, 2)]);
        series[new DateOnly(2024, 1, 4)].Should().Be(series[new DateOnly(2024, 1, 2)]);
    }

    [Fact]
    public async Task Service_CarriesTheFirstPublishedRateBackwards_ForDaysBeforeItStarts()
    {
        // Range opens 30 Dec but the feed's first quote is 1 Jan.
        var (nbu, _) = BuildNbu(NbuBody);
        var service = BuildService(nbu);

        var series = await service.GetDailySeriesAsync(
            "UAH", new DateOnly(2023, 12, 30), new DateOnly(2024, 1, 2));

        series[new DateOnly(2023, 12, 30)].Should().Be(series[new DateOnly(2024, 1, 1)]);
    }

    [Fact]
    public async Task PublishedSeries_ReturnsOnlyPublishedDays_WidenedBackwardByTheLookback()
    {
        // 3-4 Jan are an unpublished weekend and stay absent; 1 Jan sits before the range but inside the lookback.
        var (nbu, handler) = BuildNbu(NbuBody);
        var service = BuildService(nbu);

        var series = await service.GetPublishedSeriesAsync(
            "UAH", new DateOnly(2024, 1, 3), new DateOnly(2024, 1, 4));

        series.Keys.Should().BeEquivalentTo([new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)]);
        handler.LastRequestUri!.Query.Should().Contain("start=20231227").And.Contain("end=20240104");
    }

    [Fact]
    public async Task PublishedSeries_IsFetchedOncePerRange_AndAnEmptyAnswerIsNotCached()
    {
        var (nbu, handler) = BuildNbu(NbuBody);
        var service = BuildService(nbu);
        var (down, downHandler) = BuildNbu("nonsense", HttpStatusCode.InternalServerError);
        var failing = BuildService(down);
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 1, 2);

        await service.GetPublishedSeriesAsync("UAH", from, to);
        await service.GetPublishedSeriesAsync("UAH", from, to);
        (await failing.GetPublishedSeriesAsync("UAH", from, to)).Should().BeEmpty();
        await failing.GetPublishedSeriesAsync("UAH", from, to);

        handler.Calls.Should().Be(1);
        downHandler.Calls.Should().Be(2);
    }

    [Theory]
    [InlineData(2024, 1, 2, FxStatus.Exact, 2)]
    [InlineData(2024, 1, 4, FxStatus.NearestPublished, 2)]
    [InlineData(2024, 1, 9, FxStatus.NearestPublished, 2)]
    public void PublishedRateLookup_ResolvesExactThenNearestEarlier_WithinTheLookback(
        int year, int month, int day, FxStatus status, int fromDay)
    {
        var published = new Dictionary<DateOnly, decimal>
        {
            [new DateOnly(2024, 1, 1)] = 0.0263m,
            [new DateOnly(2024, 1, 2)] = 0.0264m,
        };

        var resolved = PublishedRateLookup.Resolve(published, new DateOnly(year, month, day));

        resolved.Should().NotBeNull();
        resolved!.Value.Status.Should().Be(status);
        resolved.Value.Rate.Should().Be(published[new DateOnly(2024, 1, fromDay)]);
    }

    [Fact]
    public void PublishedRateLookup_ReturnsNull_PastTheLookbackOrBeforeTheFirstRate()
    {
        var published = new Dictionary<DateOnly, decimal> { [new DateOnly(2024, 1, 2)] = 0.0264m };

        PublishedRateLookup.Resolve(published, new DateOnly(2024, 1, 10)).Should().BeNull();
        PublishedRateLookup.Resolve(published, new DateOnly(2024, 1, 1)).Should().BeNull();
    }

    [Fact]
    public async Task Service_FallsBackToTheLiveRate_WhenNoFeedCanAnswer()
    {
        var service = BuildService();

        var series = await service.GetDailySeriesAsync(
            "UAH", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 3));

        // Every day resolves rather than leaving the caller with an empty chart.
        series.Should().HaveCount(3);
        series.Values.Should().AllSatisfy(v => v.Should().BeGreaterThan(0m));
    }

    [Fact]
    public async Task Service_ReturnsEmpty_ForAnInvertedRange()
    {
        var service = BuildService();

        var series = await service.GetDailySeriesAsync(
            "UAH", new DateOnly(2024, 2, 1), new DateOnly(2024, 1, 1));

        series.Should().BeEmpty();
    }

    [Fact]
    public async Task Service_RoutesByCurrency_PreferringTheFeedThatCarriesIt()
    {
        var (nbu, nbuHandler) = BuildNbu(NbuBody);
        var frankfurter = BuildFrankfurter(
            """{"amount":1.0,"base":"EUR","rates":{"2024-01-02":{"USD":1.0956}}}""");
        var service = BuildService(nbu, frankfurter);

        var eur = await service.GetDailySeriesAsync(
            "EUR", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2));

        eur[new DateOnly(2024, 1, 2)].Should().Be(1.0956m);
        // The UAH-only feed was never called for a euro window.
        nbuHandler.LastRequestUri.Should().BeNull();
    }
}
