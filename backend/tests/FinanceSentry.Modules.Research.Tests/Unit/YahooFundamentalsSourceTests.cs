namespace FinanceSentry.Modules.Research.Tests.Unit;

using System.Net;
using System.Text;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// #837: <see cref="YahooFundamentalsSource"/> over recorded fundamentals-timeseries responses
/// (<c>Fixtures/Yahoo/</c>, captured 2026-10-07): GRAB (a foreign private issuer whose quarters EDGAR
/// cannot supply) and TLT (an ETF — every series empty).
/// </summary>
public sealed class YahooFundamentalsSourceTests
{
    private static readonly DateTimeOffset CaptureDate = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Unit", "Fixtures", "Yahoo");

    [Fact]
    public async Task Grab_ParsesFiveQuartersAndFourYears_OfRevenueAndOperatingIncome()
    {
        var (sut, _) = CreateSut(_ => Json(Fixture("GRAB")));

        var result = await sut.FetchAsync("GRAB", CancellationToken.None);

        result.Outcome.Should().Be(FundamentalsSourceOutcome.Facts);
        var quarters = result.Facts.Where(f => f.FiscalPeriod == "3M").ToList();
        var years = result.Facts.Where(f => f.FiscalPeriod == "FY").ToList();
        quarters.Where(f => f.Concept == "Revenue").Select(f => f.Value).Should().Equal(
            998_000_000m, 955_000_000m, 906_000_000m, 873_000_000m, 819_000_000m);
        quarters.Where(f => f.Concept == "OperatingIncome").Select(f => f.Value).Should().Equal(
            93_000_000m, 74_000_000m, 98_000_000m, 67_000_000m, 39_000_000m);
        quarters.Single(f => f.Concept == "Revenue" && f.PeriodEnd == new DateOnly(2026, 6, 30)).Unit.Should().Be("USD");
        years.Where(f => f.Concept == "Revenue").Should().HaveCount(4);
        years.Single(f => f.Concept == "DilutedEPS" && f.PeriodEnd == new DateOnly(2025, 12, 31))
            .Should().Match<FundamentalFact>(f => f.Value == 0.06m && f.Unit == "USD/shares");
        result.Facts.Select(f => f.Concept).Distinct().Should().BeEquivalentTo(
            "Revenue", "GrossProfit", "OperatingIncome", "NetIncome", "DilutedEPS", "StockholdersEquity");
    }

    [Fact]
    public async Task EveryFact_CarriesYahooProvenance_AndNoFilingForm()
    {
        var (sut, _) = CreateSut(_ => Json(Fixture("GRAB")));

        var facts = (await sut.FetchAsync("GRAB", CancellationToken.None)).Facts;

        facts.Should().OnlyContain(f =>
            f.SourceProvenance != null
            && f.SourceProvenance.Provider == FundamentalsSourceNames.YahooFinance
            && f.SourceProvenance.DocumentUrl == YahooFundamentalsSource.DocumentUrl("GRAB")
            && f.SourceProvenance.IngestedAt == CaptureDate
            && f.Form == string.Empty
            && f.Taxonomy == YahooFundamentalsSource.YahooTaxonomy
            && f.FiscalYear == null);
    }

    [Fact]
    public async Task Etf_WithEmptySeries_IsNoData()
    {
        var (sut, _) = CreateSut(_ => Json(Fixture("TLT")));

        var result = await sut.FetchAsync("TLT", CancellationToken.None);

        result.Outcome.Should().Be(FundamentalsSourceOutcome.NoData);
    }

    [Fact]
    public async Task RateLimited_IsFailed_AndRetriedNextTime()
    {
        var (sut, calls) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var first = await sut.FetchAsync("GRAB", CancellationToken.None);
        await sut.FetchAsync("GRAB", CancellationToken.None);

        first.Outcome.Should().Be(FundamentalsSourceOutcome.Failed);
        calls().Should().Be(2);
    }

    [Fact]
    public async Task Answer_IsCachedPerTicker()
    {
        var (sut, calls) = CreateSut(_ => Json(Fixture("GRAB")));

        await sut.FetchAsync("GRAB", CancellationToken.None);
        await sut.FetchAsync("grab", CancellationToken.None);

        calls().Should().Be(1);
    }

    private static string Fixture(string ticker)
        => File.ReadAllText(Path.Combine(FixtureDir, $"{ticker}-fundamentals-timeseries.json"));

    private static (YahooFundamentalsSource Sut, Func<int> Calls) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var calls = 0;
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(YahooFundamentalsSource.HttpClientName))
            .Returns(() => new HttpClient(new DelegatingStub(request =>
            {
                Interlocked.Increment(ref calls);
                return respond(request);
            })));
        var sut = new YahooFundamentalsSource(
            factory.Object, new FixedTimeProvider(CaptureDate), NullLogger<YahooFundamentalsSource>.Instance);
        return (sut, () => calls);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class DelegatingStub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
