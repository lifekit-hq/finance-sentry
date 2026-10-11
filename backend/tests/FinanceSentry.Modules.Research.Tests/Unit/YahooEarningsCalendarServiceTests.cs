namespace FinanceSentry.Modules.Research.Tests.Unit;

using System.Net;
using System.Text;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Ports;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class YahooEarningsCalendarServiceTests
{
    private const string CrumbBody = "test-crumb";

    [Fact]
    public async Task GetForTickersAsync_AllTickersFail_SurfaceProviderFailureFalse_ReturnsEmpty()
    {
        var sut = CreateSut(request => request.RequestUri!.AbsoluteUri.Contains("getcrumb")
            ? Text(CrumbBody)
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await sut.GetForTickersAsync(["AAPL", "MU"], DateOnly.MinValue, DateOnly.MaxValue, null);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetForTickersAsync_AllTickersFail_SurfaceProviderFailureTrue_Throws()
    {
        var sut = CreateSut(request => request.RequestUri!.AbsoluteUri.Contains("getcrumb")
            ? Text(CrumbBody)
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var act = () => sut.GetForTickersAsync(
            ["AAPL", "MU"], DateOnly.MinValue, DateOnly.MaxValue, null, surfaceProviderFailure: true);

        await act.Should().ThrowAsync<EarningsCalendarProviderException>();
    }

    [Fact]
    public async Task GetForTickersAsync_PartialFailure_SurfaceProviderFailureTrue_DoesNotThrow()
    {
        var sut = CreateSut(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.Contains("getcrumb"))
            {
                return Text(CrumbBody);
            }

            return url.Contains("/AAPL")
                ? Json("""{"quoteSummary":{"result":[{"calendarEvents":{}}]}}""")
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        var act = () => sut.GetForTickersAsync(
            ["AAPL", "MU"], DateOnly.MinValue, DateOnly.MaxValue, null, surfaceProviderFailure: true);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetForTickersAsync_CrumbFetchFails_SurfaceProviderFailureTrue_Throws()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var act = () => sut.GetForTickersAsync(
            ["AAPL"], DateOnly.MinValue, DateOnly.MaxValue, null, surfaceProviderFailure: true);

        await act.Should().ThrowAsync<EarningsCalendarProviderException>();
    }

    [Fact]
    public async Task GetForTickersAsync_CachedTicker_ExpiresAfterSixHours()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));
        var quoteCalls = 0;
        var sut = CreateSut(request =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("getcrumb"))
            {
                return Text(CrumbBody);
            }

            if (request.RequestUri!.AbsoluteUri.Contains("quoteSummary"))
            {
                quoteCalls++;
            }

            return Json("""{"quoteSummary":{"result":[{"calendarEvents":{}}]}}""");
        }, clock);

        await sut.GetForTickersAsync(["AAPL"], DateOnly.MinValue, DateOnly.MaxValue, null);
        clock.Advance(TimeSpan.FromHours(6) - TimeSpan.FromSeconds(1));
        await sut.GetForTickersAsync(["aapl"], DateOnly.MinValue, DateOnly.MaxValue, null);
        quoteCalls.Should().Be(1);

        clock.Advance(TimeSpan.FromSeconds(1));
        await sut.GetForTickersAsync(["AAPL"], DateOnly.MinValue, DateOnly.MaxValue, null);
        quoteCalls.Should().Be(2);
    }

    [Fact]
    public async Task GetForTickersAsync_ConcurrentCallsForOneTicker_QueryYahooOnce()
    {
        var quoteCalls = 0;
        var sut = CreateSut(request =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("getcrumb"))
            {
                return Text(CrumbBody);
            }

            if (request.RequestUri!.AbsoluteUri.Contains("quoteSummary"))
            {
                Interlocked.Increment(ref quoteCalls);
            }

            return Json("""{"quoteSummary":{"result":[{"calendarEvents":{}}]}}""");
        });

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            sut.GetForTickersAsync(["AAPL"], DateOnly.MinValue, DateOnly.MaxValue, null)));

        quoteCalls.Should().Be(1);
    }

    private static YahooEarningsCalendarService CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage> respond, TimeProvider? clock = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(YahooEarningsCalendarService.HttpClientName))
            .Returns(() => new HttpClient(new DelegatingStub(respond)));
        return new YahooEarningsCalendarService(factory.Object, clock ?? TimeProvider.System, NullLogger<YahooEarningsCalendarService>.Instance);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Text(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private sealed class DelegatingStub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
