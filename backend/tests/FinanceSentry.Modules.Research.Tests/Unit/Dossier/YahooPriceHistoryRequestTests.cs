namespace FinanceSentry.Modules.Research.Tests.Unit.Dossier;

using System.Net;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class YahooPriceHistoryRequestTests
{
    private const string EmptyChart = """{"chart":{"result":[]}}""";

    // The dossier price block cuts every period out of the MAX series, so MAX must be daily bars:
    // monthly ones would leave the 1M, 6M and 1Y tabs with a handful of month-end points.
    [Theory]
    [InlineData("MAX", "interval=1d&range=max")]
    [InlineData("1Y", "interval=1d&range=1y")]
    [InlineData("1M", "interval=1d&range=1mo")]
    [InlineData("6M", "interval=1d&range=6mo")]
    [InlineData("YTD", "interval=1d&range=ytd")]
    [InlineData("5Y", "interval=1wk&range=5y")]
    public async Task GetAsync_AsksYahooForTheIntervalAndRangeOfThePeriod(string range, string expectedQuery)
    {
        var handler = new RecordingHandler();
        using var service = ServiceOver(handler);

        await service.GetAsync("AAPL", range);

        var request = handler.Requested.Should().ContainSingle().Subject;
        request.AbsolutePath.Should().Be("/v8/finance/chart/AAPL");
        request.Query.TrimStart('?').Should().Be(expectedQuery);
    }

    private static YahooPriceHistoryService ServiceOver(RecordingHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://yahoo.test") });
        return new YahooPriceHistoryService(factory.Object, TimeProvider.System, NullLogger<YahooPriceHistoryService>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requested.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyChart) });
        }
    }
}
