namespace FinanceSentry.Modules.Research.Tests.Unit;

using System.Net;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class YahooMarketDataServiceTests
{
    [Fact]
    public async Task GetQuotesAsync_UsesMhpcYahooAlias_AndKeepsRequestedTicker()
    {
        var handler = new StubHttpMessageHandler(_ => QuoteJson(
            symbol: "MHPC.IL",
            marketState: "REGULAR",
            regularMarketPrice: 8.30m,
            chartPreviousClose: 8.32m));
        var sut = CreateSut(handler);

        var result = await sut.GetQuotesAsync(["MHPC"]);

        handler.RequestedPath.Should().Contain("/v8/finance/chart/MHPC.IL");
        result.Should().ContainKey("MHPC");
        result["MHPC"].Ticker.Should().Be("MHPC");
        result["MHPC"].ResolvedTicker.Should().Be("MHPC.IL");
        result["MHPC"].Price.Should().Be(8.30m);
    }

    [Fact]
    public async Task GetQuotesAsync_UsesPreMarketPrice_WhenYahooMarketStateIsPre()
    {
        var handler = new StubHttpMessageHandler(_ => QuoteJson(
            symbol: "DRAM",
            marketState: "PRE",
            regularMarketPrice: 63.04m,
            chartPreviousClose: 63.04m,
            preMarketPrice: 56m));
        var sut = CreateSut(handler);

        var result = await sut.GetQuotesAsync(["DRAM"]);

        result["DRAM"].Price.Should().Be(56m);
        result["DRAM"].Session.Should().Be("pre_market");
        result["DRAM"].IsStale.Should().BeFalse();
    }

    /// <summary>
    /// On the 5-day chart, chartPreviousClose is the close before the whole window — a weekly baseline.
    /// The previous close is the prior session's bar, so a quote's change is a daily one.
    /// </summary>
    [Fact]
    public async Task GetQuotesAsync_PreviousCloseIsThePriorSessionsBar_NotTheChartWindowBaseline()
    {
        // regularMarketTime 1783958400 = 2026-07-13 16:00 UTC; bars run 07-07 .. 07-13 at 13:30 UTC.
        var handler = new StubHttpMessageHandler(_ => QuoteJson(
            symbol: "PSX",
            marketState: "REGULAR",
            regularMarketPrice: 273.13m,
            chartPreviousClose: 259.47m,
            bars: [(1783431000, 262m), (1783517400, 265m), (1783603800, 268m), (1783690200, 271.5m), (1783949400, 273.13m)]));
        var sut = CreateSut(handler);

        var result = await sut.GetQuotesAsync(["PSX"]);

        result["PSX"].PreviousClose.Should().Be(271.5m);
    }

    [Fact]
    public async Task GetQuotesAsync_RefusesUnexpectedYahooSymbol()
    {
        var handler = new StubHttpMessageHandler(_ => QuoteJson(
            symbol: "WRONG",
            marketState: "REGULAR",
            regularMarketPrice: 0.0004m,
            chartPreviousClose: 0.0004m));
        var cache = new Mock<IQuoteCacheRepository>();
        cache.Setup(c => c.GetFreshAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, QuoteCacheEntry>());
        var sut = CreateSut(handler, cache);

        var result = await sut.GetQuotesAsync(["MHPCX"]);

        result.Should().BeEmpty();
        cache.Verify(c => c.UpsertManyAsync(It.IsAny<IReadOnlyCollection<QuoteCacheEntry>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static YahooMarketDataService CreateSut(
        HttpMessageHandler handler,
        Mock<IQuoteCacheRepository>? cacheMock = null)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://query1.finance.yahoo.com"),
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(YahooMarketDataService.HttpClientName)).Returns(http);

        var cache = cacheMock ?? new Mock<IQuoteCacheRepository>();
        cache.Setup(c => c.GetFreshAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, QuoteCacheEntry>());
        cache.Setup(c => c.UpsertManyAsync(It.IsAny<IReadOnlyCollection<QuoteCacheEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new YahooMarketDataService(factory.Object, cache.Object, NullLogger<YahooMarketDataService>.Instance);
    }

    private static string QuoteJson(
        string symbol,
        string marketState,
        decimal regularMarketPrice,
        decimal chartPreviousClose,
        decimal? preMarketPrice = null,
        decimal? postMarketPrice = null,
        (long Timestamp, decimal Close)[]? bars = null)
    {
        var preMarket = preMarketPrice is null ? string.Empty : $@",""preMarketPrice"":{preMarketPrice}";
        var postMarket = postMarketPrice is null ? string.Empty : $@",""postMarketPrice"":{postMarketPrice}";
        var series = bars is null
            ? string.Empty
            : $@",""timestamp"":[{string.Join(',', bars.Select(b => b.Timestamp))}]," +
              $@"""indicators"":{{""quote"":[{{""close"":[{string.Join(',', bars.Select(b => b.Close.ToString(System.Globalization.CultureInfo.InvariantCulture)))}]}}]}}";
        return $$"""
        {
          "chart": {
            "result": [
              {
                "meta": {
                  "symbol": "{{symbol}}",
                  "currency": "USD",
                  "marketState": "{{marketState}}",
                  "regularMarketPrice": {{regularMarketPrice}},
                  "chartPreviousClose": {{chartPreviousClose}},
                  "regularMarketTime": 1783958400,
                  "preMarketTime": 1783947600,
                  "postMarketTime": 1783976400
                  {{preMarket}}
                  {{postMarket}}
                }
                {{series}}
              }
            ]
          }
        }
        """;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, string> responseFactory) : HttpMessageHandler
    {
        public string? RequestedPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedPath = request.RequestUri?.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseFactory(request)),
            });
        }
    }
}
