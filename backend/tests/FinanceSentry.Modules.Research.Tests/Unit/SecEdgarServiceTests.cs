namespace FinanceSentry.Modules.Research.Tests.Unit;

using System.Net;
using System.Text;
using FinanceSentry.Modules.Research.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class SecEdgarServiceTests
{
    private const string TickerMapJson = """{"0":{"cik_str":320193,"ticker":"AAPL","title":"Apple Inc."}}""";

    private const string SubmissionsJson = """
        {"filings":{"recent":{
          "form":["10-Q"],
          "filingDate":["2026-09-21"],
          "reportDate":["2026-06-30"],
          "accessionNumber":["0000320193-26-000123"],
          "primaryDocument":["aapl-20260630.htm"],
          "primaryDocDescription":["10-Q"],
          "isXBRL":[1]
        }}}
        """;

    [Fact]
    public async Task GetRecentFilingsAsync_FailedSubmissionsFetch_IsNotCached()
    {
        var submissionsCalls = 0;
        var sut = CreateSut(request =>
        {
            if (request.RequestUri!.Host == "www.sec.gov")
            {
                return Json(TickerMapJson);
            }

            submissionsCalls++;
            return submissionsCalls == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json(SubmissionsJson);
        });

        var first = await sut.GetRecentFilingsAsync("AAPL", ["10-Q"], 10);
        var second = await sut.GetRecentFilingsAsync("AAPL", ["10-Q"], 10);

        first.Should().BeEmpty();
        second.Should().ContainSingle(f => f.AccessionNumber == "0000320193-26-000123");
        submissionsCalls.Should().Be(2);
    }

    [Fact]
    public async Task GetRecentFilingsAsync_FailedSubmissionsFetch_SurfaceProviderFailureTrue_Throws()
    {
        var sut = CreateSut(request =>
        {
            if (request.RequestUri!.Host == "www.sec.gov")
            {
                return Json(TickerMapJson);
            }

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        var act = () => sut.GetRecentFilingsAsync("AAPL", ["10-Q"], 10, surfaceProviderFailure: true);

        await act.Should().ThrowAsync<EdgarProviderException>();
    }

    [Fact]
    public async Task GetRecentFilingsAsync_TickerNotAFiler_SurfaceProviderFailureTrue_StaysEmpty()
    {
        var sut = CreateSut(request =>
        {
            if (request.RequestUri!.Host == "www.sec.gov")
            {
                return Json(TickerMapJson); // a real map that just doesn't contain NOTAFILER
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var result = await sut.GetRecentFilingsAsync("NOTAFILER", ["10-Q"], 10, surfaceProviderFailure: true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentFilingsAsync_TickerMapFetchFailed_SurfaceProviderFailureTrue_Throws()
    {
        var sut = CreateSut(request => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var act = () => sut.GetRecentFilingsAsync("AAPL", ["10-Q"], 10, surfaceProviderFailure: true);

        await act.Should().ThrowAsync<EdgarProviderException>();
    }

    [Fact]
    public async Task GetRecentFilingsAsync_SuccessfulSubmissionsFetch_IsCached()
    {
        var submissionsCalls = 0;
        var sut = CreateSut(request =>
        {
            if (request.RequestUri!.Host == "www.sec.gov")
            {
                return Json(TickerMapJson);
            }

            submissionsCalls++;
            return Json(SubmissionsJson);
        });

        await sut.GetRecentFilingsAsync("AAPL", ["10-Q"], 10);
        var second = await sut.GetRecentFilingsAsync("AAPL", ["10-Q"], 10);

        second.Should().ContainSingle();
        submissionsCalls.Should().Be(1);
    }

    private static SecEdgarService CreateSut(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(SecEdgarService.HttpClientName))
            .Returns(() => new HttpClient(new DelegatingStub(respond)));
        return new SecEdgarService(factory.Object, NullLogger<SecEdgarService>.Instance);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class DelegatingStub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
