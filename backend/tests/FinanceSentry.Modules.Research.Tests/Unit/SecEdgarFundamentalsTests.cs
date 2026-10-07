namespace FinanceSentry.Modules.Research.Tests.Unit;

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// #837: <see cref="SecEdgarService.GetFundamentalsAsync"/> over recorded EDGAR companyconcept
/// responses (<c>Fixtures/Edgar/</c>, captured 2026-10-07; anything not recorded answers 404, as
/// EDGAR does for a tag the filer never used). GRAB is a foreign private issuer filing IFRS 20-Fs
/// with no us-gaap fundamentals; DUOL is a domestic 10-K/10-Q filer whose output is pinned to the
/// snapshot taken before the 20-F path existed.
/// </summary>
public sealed class SecEdgarFundamentalsTests
{
    private const int MaxPerConcept = 20;

    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Unit", "Fixtures", "Edgar");

    private readonly ConcurrentBag<string> requestedPaths = [];

    [Fact]
    public async Task GetFundamentalsAsync_IfrsForeignPrivateIssuer_ReadsIfrsFull20FFacts()
    {
        var sut = CreateFixtureSut();

        var facts = await sut.GetFundamentalsAsync("GRAB", MaxPerConcept);

        facts.Should().NotBeEmpty();
        facts.Should().OnlyContain(f => f.Taxonomy == FundamentalFact.IfrsTaxonomy && f.Form == "20-F");
        facts.Select(f => f.Concept).Distinct().Should().BeEquivalentTo(
            "Revenue", "OperatingIncome", "NetIncome", "DilutedEPS", "StockholdersEquity");

        Fact(facts, "Revenue", new DateOnly(2025, 12, 31)).Value.Should().Be(3_370_000_000m);
        Fact(facts, "NetIncome", new DateOnly(2025, 12, 31)).Value.Should().Be(268_000_000m);
        Fact(facts, "OperatingIncome", new DateOnly(2025, 12, 31)).Value.Should().Be(65_000_000m);
        Fact(facts, "DilutedEPS", new DateOnly(2025, 12, 31)).Unit.Should().Be("USD/shares");
        Fact(facts, "StockholdersEquity", new DateOnly(2025, 12, 31)).Value.Should().Be(6_728_000_000m);
    }

    [Fact]
    public async Task Evaluate_IfrsForeignPrivateIssuer_ScoresOnAnnualBasis()
    {
        var sut = CreateFixtureSut();
        var facts = await sut.GetFundamentalsAsync("GRAB", FundamentalsScorer.FactsPerConcept);

        var result = FundamentalsScorer.Evaluate(facts);

        // Before this step the quarterly-only scorer returned a null score for GRAB.
        result.Basis.Should().Be(FundamentalsScorer.AnnualBasis);
        // EPS went -0.03 -> +0.06: growth off a loss base is not evaluable, not -300%.
        result.Score.Should().Be(70);
        result.RevenueYoy.Should().BeApproximately(0.2048623m, 0.0000001m);
        result.EpsYoy.Should().BeNull();
        result.NotEvaluableReasons.Should().Contain("eps_yoy_not_evaluable");
        result.NotEvaluableReasons.Should().Contain("gross_margin_not_evaluable");
    }

    [Fact]
    public async Task GetFundamentalsAsync_IfrsRevenueAndContractRevenueOverlap_KeepsTotalRevenue()
    {
        var sut = CreateFixtureSut();

        var facts = await sut.GetFundamentalsAsync("GRAB", MaxPerConcept);

        // FY2022: Revenue (total) is 1,433M; RevenueFromContractsWithCustomers is a 52M sub-line.
        Fact(facts, "Revenue", new DateOnly(2022, 12, 31)).Value.Should().Be(1_433_000_000m);
    }

    [Fact]
    public async Task GetFundamentalsAsync_IfrsForeignPrivateIssuer_Skips6KFurnishings()
    {
        var sut = CreateFixtureSut();

        var facts = await sut.GetFundamentalsAsync("GRAB", MaxPerConcept);

        // The fixtures carry half-year 6-K points (e.g. 2025-06-30); they are not periodic statements.
        facts.Should().NotContain(f => f.PeriodEnd == new DateOnly(2025, 6, 30));
    }

    [Fact]
    public async Task GetFundamentalsAsync_DomesticFiler_OutputUnchangedFromPre20FSnapshot()
    {
        var sut = CreateFixtureSut();
        var expected = JsonSerializer.Deserialize<List<FactShape>>(
            await File.ReadAllTextAsync(Path.Combine(FixtureDir, "DUOL-fundamentals-pre-20f.json")))!;

        var facts = await sut.GetFundamentalsAsync("DUOL", MaxPerConcept);

        facts.Select(FactShape.From).Should().Equal(expected);
        facts.Should().OnlyContain(f => f.Taxonomy == FundamentalFact.UsGaapTaxonomy);
    }

    [Fact]
    public async Task GetFundamentalsAsync_DomesticFiler_NeverQueriesIfrsFull()
    {
        var sut = CreateFixtureSut();

        await sut.GetFundamentalsAsync("DUOL", MaxPerConcept);

        requestedPaths.Should().NotContain(p => p.Contains("/ifrs-full/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetFundamentalsAsync_UsGaapFactsOn20F_AreKeptWithoutIfrsFallback()
    {
        // A foreign private issuer that reports in us-gaap (e.g. BABA) files its annual numbers on 20-F.
        const string tickerMap = """{"0":{"cik_str":1577552,"ticker":"BABA","title":"Alibaba Group Holding Ltd"}}""";
        const string revenues = """
            {"label":"Revenues","units":{"USD":[
              {"start":"2024-04-01","end":"2025-03-31","val":137300000000,"fy":2025,"fp":"FY","form":"20-F"},
              {"start":"2025-04-01","end":"2025-09-30","val":70000000000,"form":"6-K"}
            ]}}
            """;
        var sut = CreateSut(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requestedPaths.Add(path);
            if (request.RequestUri.Host == "www.sec.gov")
            {
                return Json(tickerMap);
            }

            return path.EndsWith("/us-gaap/Revenues.json", StringComparison.Ordinal)
                ? Json(revenues)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var facts = await sut.GetFundamentalsAsync("BABA", MaxPerConcept);

        facts.Should().ContainSingle().Which.Should().Match<FundamentalFact>(f =>
            f.Concept == "Revenue" && f.Form == "20-F" && f.Taxonomy == FundamentalFact.UsGaapTaxonomy
            && f.Value == 137_300_000_000m);
        requestedPaths.Should().NotContain(p => p.Contains("/ifrs-full/", StringComparison.Ordinal));
    }

    private static FundamentalFact Fact(IReadOnlyList<FundamentalFact> facts, string concept, DateOnly periodEnd)
        => facts.Single(f => f.Concept == concept && f.PeriodEnd == periodEnd);

    private SecEdgarService CreateFixtureSut() => CreateSut(request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        requestedPaths.Add(path);

        string? file = null;
        if (path.EndsWith("/company_tickers.json", StringComparison.Ordinal))
        {
            file = "company_tickers-subset.json";
        }
        else if (path.StartsWith("/api/xbrl/companyconcept/", StringComparison.Ordinal))
        {
            // /api/xbrl/companyconcept/CIK0001855612/ifrs-full/Revenue.json -> CIK0001855612-ifrs-full-Revenue.json
            file = path["/api/xbrl/companyconcept/".Length..].Replace('/', '-');
        }

        var fullPath = file is null ? null : Path.Combine(FixtureDir, file);
        return fullPath is not null && File.Exists(fullPath)
            ? Json(File.ReadAllText(fullPath))
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    });

    private static SecEdgarService CreateSut(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(SecEdgarService.HttpClientName))
            .Returns(() => new HttpClient(new DelegatingStub(respond)));
        return new SecEdgarService(factory.Object, NullLogger<SecEdgarService>.Instance);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // The fact fields that existed before #837 added Taxonomy, as the pre-20-F snapshot recorded them.
    private sealed record FactShape(
        string Ticker,
        string Concept,
        string Label,
        string Unit,
        decimal Value,
        DateOnly PeriodEnd,
        string? FiscalPeriod,
        int? FiscalYear,
        string Form)
    {
        public static FactShape From(FundamentalFact f)
            => new(f.Ticker, f.Concept, f.Label, f.Unit, f.Value, f.PeriodEnd, f.FiscalPeriod, f.FiscalYear, f.Form);
    }

    private sealed class DelegatingStub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
