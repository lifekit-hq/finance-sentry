namespace FinanceSentry.Modules.Research.Tests.Companion;

using System.Net;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public sealed class FinnhubRecommendationTrendsServiceTests
{
    private const string MuJson = """
    [ { "symbol": "MU", "period": "2026-08-01", "strongBuy": 14, "buy": 20, "hold": 6, "sell": 1, "strongSell": 0 } ]
    """;

    [Fact]
    public void IsConfigured_false_without_key_and_true_with_key()
    {
        CreateSut(new StubHandler(_ => Ok(MuJson)), apiKey: "").IsConfigured.Should().BeFalse();
        CreateSut(new StubHandler(_ => Ok(MuJson)), apiKey: "k").IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_false_when_disabled_even_with_key()
    {
        CreateSut(new StubHandler(_ => Ok(MuJson)), apiKey: "k", enabled: false)
            .IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAsync_returns_empty_without_key_and_makes_no_requests()
    {
        var handler = new StubHandler(_ => Ok(MuJson));
        var sut = CreateSut(handler, apiKey: "");

        var result = await sut.FetchAsync(["MU"]);

        result.Should().BeEmpty();
        handler.Requests.Should().BeEmpty("an unconfigured source must not call the provider");
    }

    [Fact]
    public async Task FetchAsync_maps_and_stamps_source_and_canonical_ticker()
    {
        var handler = new StubHandler(_ => Ok(MuJson));
        var sut = CreateSut(handler);

        var result = await sut.FetchAsync(["mu"]);

        result.Should().HaveCount(1);
        result[0].Ticker.Should().Be("MU");
        result[0].Source.Should().Be("finnhub");
        result[0].StrongBuy.Should().Be(14);
        handler.Requests.Should().ContainSingle(p => p.Contains("stock/recommendation?symbol=MU"));
    }

    [Fact]
    public async Task FetchAsync_never_puts_the_token_in_the_url()
    {
        var handler = new StubHandler(_ => Ok(MuJson));
        var sut = CreateSut(handler, apiKey: "super-secret");

        await sut.FetchAsync(["MU"]);

        handler.Requests.Should().OnlyContain(p => !p.Contains("super-secret") && !p.Contains("token="));
    }

    [Fact]
    public async Task FetchAsync_swallows_per_ticker_server_errors_but_keeps_others()
    {
        var handler = new StubHandler(req =>
            req.RequestUri!.Query.Contains("BAD")
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : Ok(MuJson));
        var sut = CreateSut(handler);

        var result = await sut.FetchAsync(["BAD", "MU"]);

        result.Should().ContainSingle(t => t.Ticker == "MU");
    }

    [Fact]
    public async Task FetchAsync_throws_when_every_ticker_fails()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut(handler);

        var act = () => sut.FetchAsync(["A", "B"]);

        await act.Should().ThrowAsync<AnalystSourceParseException>(
            "an all-tickers failure means the provider is broken and the health path must fire");
    }

    [Fact]
    public async Task FetchAsync_throws_immediately_on_auth_failure()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var sut = CreateSut(handler);

        var act = () => sut.FetchAsync(["A", "B", "C"]);

        await act.Should().ThrowAsync<AnalystSourceParseException>();
        handler.Requests.Should().HaveCount(1, "auth failure is global — do not hammer the other tickers");
    }

    [Fact]
    public async Task FetchAsync_does_not_retry_429_itself_and_skips_the_ticker()
    {
        var attempts = 0;
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.Query.Contains("MU"))
            {
                attempts++;
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            }

            return Ok("""[ { "symbol": "NVDA", "period": "2026-08-01", "buy": 3 } ]""");
        });
        var sut = CreateSut(handler);

        var result = await sut.FetchAsync(["MU", "NVDA"]);

        attempts.Should().Be(1, "retrying a 429 is the registered client's resilience handler's job, not the service's");
        result.Should().ContainSingle(t => t.Ticker == "NVDA");
    }

    [Fact]
    public async Task FetchAsync_returns_empty_for_empty_universe()
    {
        var handler = new StubHandler(_ => Ok(MuJson));
        var sut = CreateSut(handler);

        (await sut.FetchAsync([])).Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    private static FinnhubRecommendationTrendsService CreateSut(
        StubHandler handler, string apiKey = "test-key", bool enabled = true)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://finnhub.io/api/v1/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(FinnhubRecommendationTrendsService.HttpClientName)).Returns(http);

        var options = Options.Create(new AnalystSourcesOptions
        {
            Finnhub = new AnalystSourcesOptions.FinnhubOptions
            {
                Enabled = enabled,
                ApiKey = apiKey,
                // 60k/min ⇒ ~1ms pacing so tests stay fast; pacing math is exercised, not the wall clock.
                RequestsPerMinute = 60_000,
            },
        });

        return new FinnhubRecommendationTrendsService(
            factory.Object, options, NullLogger<FinnhubRecommendationTrendsService>.Instance);
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(responder(request));
        }
    }
}
