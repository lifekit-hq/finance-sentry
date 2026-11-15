namespace FinanceSentry.Modules.Research.Tests.Unit;

using System.Net;
using System.Text;
using FinanceSentry.Modules.Research.Application.Services.Etf;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// Issue #1324: where an ETF's top 10 holdings come from. Yahoo <c>topHoldings</c> over a recorded response
/// (<c>Fixtures/Yahoo/SPY-top-holdings.json</c>, captured 2026-11-14), the issuer's CSV as the fallback, and the daily
/// cache over both.
/// </summary>
public sealed class FundHoldingsTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Unit", "Fixtures", "Yahoo");

    [Fact]
    public void Yahoo_ParsesTheTopTenOfAnEtf_AsPercentOfTheFund()
    {
        var lookup = YahooFundHoldingsProvider.Parse(Fixture("SPY-top-holdings"), Start);

        lookup.Kind.Should().Be(FundKind.Fund);
        lookup.Source.Should().Be(FundHoldingsSources.Yahoo);
        lookup.AsOf.Should().Be(Start);
        lookup.Holdings.Should().HaveCount(10);
        lookup.Holdings[0].Should().Be(new FundHolding("AAPL", "Apple Inc", 7.70m));
        lookup.Holdings.Select(h => h.WeightPct).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Yahoo_AnEquity_IsNotAFund()
    {
        YahooFundHoldingsProvider.Parse(Fixture("AAPL-price-only"), Start).Kind.Should().Be(FundKind.NotAFund);
    }

    [Fact]
    public void Yahoo_AnEtfWithoutTopHoldings_IsAFundWithNoHoldings()
    {
        const string json = """{"quoteSummary":{"result":[{"price":{"quoteType":"ETF"},"topHoldings":{"holdings":[]}}]}}""";

        var lookup = YahooFundHoldingsProvider.Parse(json, Start);

        lookup.Kind.Should().Be(FundKind.Fund);
        lookup.Holdings.Should().BeEmpty();
        lookup.Source.Should().BeNull();
    }

    [Fact]
    public void Yahoo_DropsLinesWithoutAWeight_KeepsALineWithoutASymbol_AndCutsToTen()
    {
        var lines = Enumerable.Range(1, 12)
            .Select(i => "{\"symbol\":\"S" + i + "\",\"holdingName\":\"N" + i + "\",\"holdingPercent\":{\"raw\":0.0" + i.ToString("00") + "}}")
            .Append("{\"symbol\":\"ZERO\",\"holdingPercent\":{\"raw\":0}}")
            .Append("{\"symbol\":\"NOWEIGHT\"}")
            .Append("{\"symbol\":\"\",\"holdingName\":\"Cash\",\"holdingPercent\":{\"raw\":0.5}}");
        var json = "{\"quoteSummary\":{\"result\":[{\"price\":{\"quoteType\":\"ETF\"},\"topHoldings\":{\"holdings\":["
            + string.Join(',', lines) + "]}}]}}";

        var lookup = YahooFundHoldingsProvider.Parse(json, Start);

        lookup.Holdings.Should().HaveCount(10);
        lookup.Holdings[0].Should().Be(new FundHolding(null, "Cash", 50m));
        lookup.Holdings.Select(h => h.Symbol).Should().NotContain(["ZERO", "NOWEIGHT"]);
    }

    [Theory]
    [InlineData("""{"quoteSummary":{"result":null,"error":{"code":"Not Found"}}}""")]
    [InlineData("""{"quoteSummary":{"result":[]}}""")]
    [InlineData("""{}""")]
    public void Yahoo_AnAbsentResult_IsUnknown(string json)
    {
        YahooFundHoldingsProvider.Parse(json, Start).Kind.Should().Be(FundKind.Unknown);
    }

    [Fact]
    public void Issuer_ReadsAnIshares_StylePreambledCsv_AndKeepsTheTopTen()
    {
        var rows = Enumerable.Range(1, 12).Select(i => $"S{i},Name {i},Equity,\"1,000.00\",{i}.00,100,100,0.00,-");
        var csv = "﻿iShares Test ETF\r\nFund Holdings as of,\"Nov 13, 2026\"\r\n\r\n"
            + "Ticker,Name,Asset Class,Market Value,Weight (%),Notional Value,Shares,Price,Location\r\n"
            + string.Join("\r\n", rows) + "\r\n-,Cash,Cash,5.00,0.01,5,5,1,-\r\n";

        var holdings = IssuerFundHoldingsProvider.Parse(csv, 10);

        holdings.Should().HaveCount(10);
        holdings[0].Should().Be(new FundHolding("S12", "Name 12", 12m));
        holdings.Select(h => h.WeightPct).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Issuer_ReadsFractionsAsPercent_AndQuotedNames()
    {
        const string csv = "Symbol,Holding,% of Net Assets\nAAA,\"Acme, Inc.\",0.30\nBBB,Beta,0.20\nCCC,Gamma,0.10\n";

        var holdings = IssuerFundHoldingsProvider.Parse(csv, 2);

        holdings.Should().Equal(new FundHolding("AAA", "Acme, Inc.", 30m), new FundHolding("BBB", "Beta", 20m));
    }

    [Fact]
    public void Issuer_StripsPercentSigns_AndSkipsUnreadableRows()
    {
        const string csv = "Ticker,Weight\nAAA,12.5%\nBBB,n/a\nCCC,\nDDD,7.5%\n";

        IssuerFundHoldingsProvider.Parse(csv, 10).Select(h => h.Symbol).Should().Equal("AAA", "DDD");
    }

    [Theory]
    [InlineData("")]
    [InlineData("just,some,columns\n1,2,3\n")]
    [InlineData("Ticker,Name\nAAA,Acme\n")]
    public void Issuer_AFileWithoutATickerAndWeightColumn_HasNoHoldings(string csv)
    {
        IssuerFundHoldingsProvider.Parse(csv, 10).Should().BeEmpty();
    }

    [Fact]
    public async Task Issuer_AFundWithNoMappedFile_IsAFundWithNoHoldings()
    {
        var (sut, calls) = IssuerProvider(new FundHoldingsOptions(), _ => throw new InvalidOperationException("not mapped"));

        var lookup = await sut.LookupAsync("VWCE", CancellationToken.None);

        lookup.Kind.Should().Be(FundKind.Fund);
        lookup.Holdings.Should().BeEmpty();
        calls().Should().Be(0);
    }

    [Fact]
    public async Task Issuer_ReadsAMappedFile_AndLabelsTheSource()
    {
        var options = new FundHoldingsOptions { IssuerFiles = { ["vwce"] = "https://issuer.example/vwce.csv" } };
        var (sut, calls) = IssuerProvider(options, _ => Csv("Ticker,Weight (%)\nAAA,60\nBBB,40\n"));

        var lookup = await sut.LookupAsync("VWCE", CancellationToken.None);

        lookup.Source.Should().Be(FundHoldingsSources.Issuer);
        lookup.Holdings.Select(h => h.Symbol).Should().Equal("AAA", "BBB");
        lookup.AsOf.Should().Be(Start);
        calls().Should().Be(1);
    }

    [Theory]
    [InlineData("http://issuer.example/insecure.csv")]
    [InlineData("not a url")]
    public async Task Issuer_NeverFetchesANonHttpsUrl(string url)
    {
        var options = new FundHoldingsOptions { IssuerFiles = { ["VWCE"] = url } };
        var (sut, calls) = IssuerProvider(options, _ => Csv("Ticker,Weight\nAAA,100\n"));

        (await sut.LookupAsync("VWCE", CancellationToken.None)).Holdings.Should().BeEmpty();
        calls().Should().Be(0);
    }

    [Fact]
    public async Task Issuer_AFailedDownload_IsAFundWithNoHoldings()
    {
        var options = new FundHoldingsOptions { IssuerFiles = { ["VWCE"] = "https://issuer.example/vwce.csv" } };
        var (sut, _) = IssuerProvider(options, _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var lookup = await sut.LookupAsync("VWCE", CancellationToken.None);

        lookup.Kind.Should().Be(FundKind.Fund);
        lookup.Holdings.Should().BeEmpty();
    }

    [Fact]
    public async Task Source_UsesYahoo_AndNeverAsksTheIssuer_WhenYahooListsHoldings()
    {
        var yahoo = new StubProvider(Lookup(FundKind.Fund, FundHoldingsSources.Yahoo, "AAA"));
        var issuer = new StubProvider(Lookup(FundKind.Fund, FundHoldingsSources.Issuer, "BBB"));
        using var sut = new FundHoldingsSource(yahoo, issuer, new ManualTimeProvider(Start));

        var lookup = await sut.GetTopHoldingsAsync("SPY");

        lookup.Source.Should().Be(FundHoldingsSources.Yahoo);
        issuer.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Source_FallsBackToTheIssuer_WhenYahooListsNone()
    {
        var yahoo = new StubProvider(new FundHoldingsLookup(FundKind.Fund, [], null, null));
        var issuer = new StubProvider(Lookup(FundKind.Fund, FundHoldingsSources.Issuer, "BBB"));
        using var sut = new FundHoldingsSource(yahoo, issuer, new ManualTimeProvider(Start));

        var lookup = await sut.GetTopHoldingsAsync("VWCE");

        lookup.Kind.Should().Be(FundKind.Fund);
        lookup.Source.Should().Be(FundHoldingsSources.Issuer);
        lookup.Holdings.Select(h => h.Symbol).Should().Equal("BBB");
    }

    [Fact]
    public async Task Source_FallsBackToTheIssuer_WhenYahooFailed_AndAFileIsMapped()
    {
        var yahoo = new StubProvider(FundHoldingsLookup.Unknown);
        var issuer = new StubProvider(Lookup(FundKind.Fund, FundHoldingsSources.Issuer, "BBB"));
        using var sut = new FundHoldingsSource(yahoo, issuer, new ManualTimeProvider(Start));

        (await sut.GetTopHoldingsAsync("VWCE")).Source.Should().Be(FundHoldingsSources.Issuer);
    }

    [Fact]
    public async Task Source_NeverAsksTheIssuer_ForANonFund()
    {
        var yahoo = new StubProvider(FundHoldingsLookup.NotAFund);
        var issuer = new StubProvider(Lookup(FundKind.Fund, FundHoldingsSources.Issuer, "BBB"));
        using var sut = new FundHoldingsSource(yahoo, issuer, new ManualTimeProvider(Start));

        (await sut.GetTopHoldingsAsync("BTC")).Kind.Should().Be(FundKind.NotAFund);
        issuer.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Source_CachesAnAnswerForADay()
    {
        var clock = new ManualTimeProvider(Start);
        var yahoo = new StubProvider(Lookup(FundKind.Fund, FundHoldingsSources.Yahoo, "AAA"));
        using var sut = new FundHoldingsSource(yahoo, new StubProvider(FundHoldingsLookup.Unknown), clock);

        await sut.GetTopHoldingsAsync("SPY");
        clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        await sut.GetTopHoldingsAsync("spy");
        yahoo.Calls.Should().Be(1);

        clock.Advance(TimeSpan.FromSeconds(1));
        await sut.GetTopHoldingsAsync("SPY");
        yahoo.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Source_CachesNotAFund_ButNotAFailedRead()
    {
        var notAFund = new StubProvider(FundHoldingsLookup.NotAFund);
        using var cached = new FundHoldingsSource(notAFund, new StubProvider(FundHoldingsLookup.Unknown), new ManualTimeProvider(Start));
        await cached.GetTopHoldingsAsync("BTC");
        await cached.GetTopHoldingsAsync("BTC");
        notAFund.Calls.Should().Be(1);

        var failing = new StubProvider(FundHoldingsLookup.Unknown);
        using var retried = new FundHoldingsSource(failing, new StubProvider(FundHoldingsLookup.Unknown), new ManualTimeProvider(Start));
        await retried.GetTopHoldingsAsync("SPY");
        await retried.GetTopHoldingsAsync("SPY");
        failing.Calls.Should().Be(2);
    }

    private static FundHoldingsLookup Lookup(FundKind kind, string source, params string[] symbols) =>
        new(kind, symbols.Select(s => new FundHolding(s, s, 10m)).ToList(), source, Start);

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(FixtureDir, $"{name}.json"));

    private static HttpResponseMessage Csv(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/csv") };

    private static (IssuerFundHoldingsProvider Sut, Func<int> Calls) IssuerProvider(
        FundHoldingsOptions options, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var calls = 0;
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(IssuerFundHoldingsProvider.HttpClientName))
            .Returns(() => new HttpClient(new Stub(request =>
            {
                Interlocked.Increment(ref calls);
                return respond(request);
            })));
        var sut = new IssuerFundHoldingsProvider(
            factory.Object, Options.Create(options), new ManualTimeProvider(Start), NullLogger<IssuerFundHoldingsProvider>.Instance);
        return (sut, () => calls);
    }

    private sealed class StubProvider(FundHoldingsLookup answer) : IFundHoldingsProvider
    {
        public int Calls { get; private set; }

        public Task<FundHoldingsLookup> LookupAsync(string symbol, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(answer);
        }
    }

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
