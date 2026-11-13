namespace FinanceSentry.Modules.Research.Tests.Screens;

using System.Net;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class FinnhubEarningsWindowSourceTests
{
    private const string Body = """
        {"earningsCalendar":[
          {"date":"2026-11-18","epsActual":null,"epsEstimate":1.2,"hour":"amc","quarter":3,"revenueEstimate":100,"symbol":"nvda","year":2026},
          {"date":"2026-11-19","symbol":"MU"},
          {"date":"not-a-date","symbol":"BAD"},
          {"date":"2026-11-20","symbol":""},
          {"date":"2026-11-21"},
          "junk"
        ]}
        """;

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private sealed class StubFactory(StubHandler handler) : IHttpClientFactory
    {
        public string? RequestedName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            RequestedName = name;
            return new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://finnhub.io/api/v1/") };
        }
    }

    private static IOptions<AnalystSourcesOptions> Settings(string key = "k", bool enabled = true)
        => Options.Create(new AnalystSourcesOptions { Finnhub = { ApiKey = key, Enabled = enabled } });

    [Fact]
    public void Parse_ReadsSymbolAndDate_SkipsRowsWithoutBoth_AndUpperCasesTheSymbol()
    {
        var items = FinnhubEarningsWindowSource.Parse(Body);

        items.Select(i => (i.Ticker, i.EventDate)).Should().Equal(
            ("NVDA", new DateOnly(2026, 11, 18)), ("MU", new DateOnly(2026, 11, 19)));
        items.Should().OnlyContain(i => i.EventType == "earnings" && i.Source == "finnhub");
    }

    [Fact]
    public void Parse_AnEmptyCalendar_IsEmpty_ButAShapeThatIsNotTheDocumentedObjectFailsLoudly()
    {
        FinnhubEarningsWindowSource.Parse("""{"earningsCalendar":[]}""").Should().BeEmpty();

        foreach (var broken in new[] { "[]", """{"other":1}""", "<html>challenge</html>" })
        {
            var act = () => FinnhubEarningsWindowSource.Parse(broken);
            act.Should().Throw<EarningsCalendarProviderException>();
        }
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("key", false)]
    public async Task WithoutAKeyOrWhenDisabled_NothingIsRequested(string key, bool expectNoRequest)
    {
        var handler = new StubHandler(HttpStatusCode.OK, Body);
        var source = new FinnhubEarningsWindowSource(new StubFactory(handler), Settings(key));

        source.IsConfigured.Should().Be(!expectNoRequest);
        var items = await source.GetWindowAsync(new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 23));

        handler.Requests.Count.Should().Be(expectNoRequest ? 0 : 1);
        (items.Count == 0).Should().Be(expectNoRequest);
        new FinnhubEarningsWindowSource(new StubFactory(handler), Settings(enabled: false)).IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task OneRequest_CarriesTheWindow_OnTheFinnhubClient()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Body);
        var factory = new StubFactory(handler);

        await new FinnhubEarningsWindowSource(factory, Settings()).GetWindowAsync(new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 23));

        factory.RequestedName.Should().Be(FinnhubRecommendationTrendsService.HttpClientName);
        handler.Requests.Should().ContainSingle().Which.PathAndQuery
            .Should().Be("/api/v1/calendar/earnings?from=2026-11-13&to=2026-11-23");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AProviderFailure_Throws_SoAnOutageIsNotReadAsNothingReporting(HttpStatusCode status)
    {
        var source = new FinnhubEarningsWindowSource(new StubFactory(new StubHandler(status, "{}")), Settings());

        var act = () => source.GetWindowAsync(new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 23));

        await act.Should().ThrowAsync<EarningsCalendarProviderException>();
    }
}
