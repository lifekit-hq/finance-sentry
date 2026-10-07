namespace FinanceSentry.Modules.Research.Tests.Unit;

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// #837: <see cref="SecEdgarService.GetTaxonomyFundamentalsAsync"/> (and the EDGAR half of the
/// provider chain) over recorded EDGAR companyconcept responses (<c>Fixtures/Edgar/</c>, captured
/// 2026-10-07; anything not recorded answers 404, as EDGAR does for a tag the filer never used).
/// GRAB is a foreign private issuer filing IFRS 20-Fs with no us-gaap fundamentals; DUOL is a
/// domestic 10-K/10-Q filer whose chain output is pinned to the snapshot taken before the 20-F path
/// existed.
/// </summary>
public sealed class SecEdgarFundamentalsTests
{
    private const int MaxPerConcept = 20;

    // The day the fixtures were captured: their newest periods are fresh as of this date.
    private static readonly DateTimeOffset CaptureDate = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Unit", "Fixtures", "Edgar");

    private readonly ConcurrentBag<string> requestedPaths = [];

    [Fact]
    public async Task IfrsForeignPrivateIssuer_ReadsIfrsFull20FFacts()
    {
        var sut = CreateFixtureSut();

        var result = await sut.GetTaxonomyFundamentalsAsync("GRAB", FundamentalFact.IfrsTaxonomy);
        var facts = result.Facts;

        result.Outcome.Should().Be(FundamentalsSourceOutcome.Facts);
        result.IssuerType.Should().Be(FundamentalsIssuerType.ForeignPrivateIssuer);
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
    public async Task EveryFact_CarriesProvenance_PointingAtItsFiling()
    {
        var sut = CreateFixtureSut();

        var facts = (await sut.GetTaxonomyFundamentalsAsync("GRAB", FundamentalFact.IfrsTaxonomy)).Facts;

        facts.Should().OnlyContain(f => f.SourceProvenance != null
            && f.SourceProvenance.Provider == FundamentalsSourceNames.SecEdgarIfrs
            && f.SourceProvenance.DocumentUrl!.StartsWith("https://www.sec.gov/Archives/edgar/data/1855612/", StringComparison.Ordinal));
        // FY2025 revenue is read from the 20-F filed in 2026 (accession 0001855612-26-000020).
        Fact(facts, "Revenue", new DateOnly(2025, 12, 31)).SourceProvenance!.DocumentUrl.Should().Be(
            "https://www.sec.gov/Archives/edgar/data/1855612/000185561226000020/0001855612-26-000020-index.htm");
    }

    [Fact]
    public async Task Evaluate_IfrsForeignPrivateIssuer_ScoresOnAnnualBasis()
    {
        var sut = CreateFixtureSut();
        var facts = (await sut.GetTaxonomyFundamentalsAsync("GRAB", FundamentalFact.IfrsTaxonomy)).Facts;

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
    public async Task IfrsRevenueAndContractRevenueOverlap_KeepsTotalRevenue()
    {
        var sut = CreateFixtureSut();

        var facts = (await sut.GetTaxonomyFundamentalsAsync("GRAB", FundamentalFact.IfrsTaxonomy)).Facts;

        // FY2022: Revenue (total) is 1,433M; RevenueFromContractsWithCustomers is a 52M sub-line.
        Fact(facts, "Revenue", new DateOnly(2022, 12, 31)).Value.Should().Be(1_433_000_000m);
    }

    [Fact]
    public async Task IfrsForeignPrivateIssuer_Skips6KFurnishings()
    {
        var sut = CreateFixtureSut();

        var facts = (await sut.GetTaxonomyFundamentalsAsync("GRAB", FundamentalFact.IfrsTaxonomy)).Facts;

        // The fixtures carry half-year 6-K points (e.g. 2025-06-30); they are not periodic statements.
        facts.Should().NotContain(f => f.PeriodEnd == new DateOnly(2025, 6, 30));
    }

    [Fact]
    public async Task IfrsFilerInUsGaap_IsNoData_NotAFailure()
    {
        var sut = CreateFixtureSut();

        var result = await sut.GetTaxonomyFundamentalsAsync("GRAB", FundamentalFact.UsGaapTaxonomy);

        result.Outcome.Should().Be(FundamentalsSourceOutcome.NoData);
        result.Facts.Should().BeEmpty();
    }

    [Fact]
    public async Task DomesticFiler_ChainOutputUnchangedFromPre20FSnapshot()
    {
        var chain = CreateEdgarChain(CreateFixtureSut());
        var expected = JsonSerializer.Deserialize<List<FactShape>>(
            await File.ReadAllTextAsync(Path.Combine(FixtureDir, "DUOL-fundamentals-pre-20f.json")))!;

        var result = await chain.GetFundamentalsAsync("DUOL", MaxPerConcept);

        result.Facts.Select(FactShape.From).Should().Equal(expected);
        result.Facts.Should().OnlyContain(f => f.Taxonomy == FundamentalFact.UsGaapTaxonomy
            && f.SourceProvenance!.Provider == FundamentalsSourceNames.SecEdgarUsGaap);
        result.Coverage.Status.Should().Be(FundamentalsCoverageStatus.Covered);
        result.Coverage.IssuerType.Should().Be(FundamentalsIssuerType.DomesticFiler);
    }

    [Fact]
    public async Task DomesticFiler_ChainNeverQueriesIfrsFull()
    {
        var chain = CreateEdgarChain(CreateFixtureSut());

        await chain.GetFundamentalsAsync("DUOL", MaxPerConcept);

        requestedPaths.Should().NotContain(p => p.Contains("/ifrs-full/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UsGaapFactsOn20F_AreKeptAsUsGaap()
    {
        // A foreign private issuer that reports in us-gaap (e.g. BABA) files its annual numbers on 20-F.
        const string revenues = """
            {"label":"Revenues","units":{"USD":[
              {"start":"2024-04-01","end":"2025-03-31","val":137300000000,"accn":"0001104659-25-061815","fy":2025,"fp":"FY","form":"20-F"},
              {"start":"2025-04-01","end":"2025-09-30","val":70000000000,"form":"6-K"}
            ]}}
            """;
        var sut = CreateSut(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requestedPaths.Add(path);
            if (request.RequestUri.Host == "www.sec.gov")
            {
                return Json(BabaTickerMap);
            }

            return path.EndsWith("/us-gaap/Revenues.json", StringComparison.Ordinal)
                ? Json(revenues)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await sut.GetTaxonomyFundamentalsAsync("BABA", FundamentalFact.UsGaapTaxonomy);

        result.IssuerType.Should().Be(FundamentalsIssuerType.ForeignPrivateIssuer);
        result.Facts.Should().ContainSingle().Which.Should().Match<FundamentalFact>(f =>
            f.Concept == "Revenue" && f.Form == "20-F" && f.Taxonomy == FundamentalFact.UsGaapTaxonomy
            && f.Value == 137_300_000_000m);
        requestedPaths.Should().NotContain(p => p.Contains("/ifrs-full/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryTagFailing_IsAFailure_NotNoData_AndIsNotCached()
    {
        // PR 896's limit: a us-gaap fetch that errored on every tag looked like "no us-gaap facts",
        // fell through to IFRS and cached the empty answer for 12 hours.
        var conceptCalls = 0;
        var sut = CreateSut(request =>
        {
            if (request.RequestUri!.Host == "www.sec.gov")
            {
                return Json(BabaTickerMap);
            }

            Interlocked.Increment(ref conceptCalls);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        var first = await sut.GetTaxonomyFundamentalsAsync("BABA", FundamentalFact.UsGaapTaxonomy);
        var callsAfterFirst = conceptCalls;
        var second = await sut.GetTaxonomyFundamentalsAsync("BABA", FundamentalFact.UsGaapTaxonomy);

        first.Outcome.Should().Be(FundamentalsSourceOutcome.Failed);
        second.Outcome.Should().Be(FundamentalsSourceOutcome.Failed);
        conceptCalls.Should().Be(callsAfterFirst * 2, "a failure is retried, never served from cache");
    }

    [Fact]
    public async Task TickerOutsideTheSecMap_IsNoData_NotSecRegistrant()
    {
        var sut = CreateFixtureSut();

        var result = await sut.GetTaxonomyFundamentalsAsync("NOSUCHTKR", FundamentalFact.UsGaapTaxonomy);

        result.Outcome.Should().Be(FundamentalsSourceOutcome.NoData);
        result.IssuerType.Should().Be(FundamentalsIssuerType.NotSecRegistrant);
    }

    [Fact]
    public async Task FilerWithFundFormsAndNoFacts_IsAnInvestmentFund()
    {
        const string tickerMap = """{"0":{"cik_str":1100663,"ticker":"TLT","title":"iShares Trust"}}""";
        const string submissions = """
            {"filings":{"recent":{
              "form":["NPORT-P","N-CSR","485BPOS"],
              "filingDate":["2026-09-25","2026-08-01","2026-06-30"],
              "reportDate":["2026-07-31","2026-05-31",""],
              "accessionNumber":["0001752724-26-000001","0001133228-26-000002","0001193125-26-000003"],
              "primaryDocument":["a.xml","b.htm","c.htm"],
              "primaryDocDescription":["","",""],
              "isXBRL":[0,0,0]}}}
            """;
        var sut = CreateSut(request =>
        {
            if (request.RequestUri!.Host == "www.sec.gov")
            {
                return Json(tickerMap);
            }

            return request.RequestUri.AbsolutePath.StartsWith("/submissions/", StringComparison.Ordinal)
                ? Json(submissions)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await sut.GetTaxonomyFundamentalsAsync("TLT", FundamentalFact.UsGaapTaxonomy);

        result.Outcome.Should().Be(FundamentalsSourceOutcome.NoData);
        result.IssuerType.Should().Be(FundamentalsIssuerType.InvestmentFund);
    }

    private const string BabaTickerMap = """{"0":{"cik_str":1577552,"ticker":"BABA","title":"Alibaba Group Holding Ltd"}}""";

    private static FundamentalFact Fact(IReadOnlyList<FundamentalFact> facts, string concept, DateOnly periodEnd)
        => facts.Single(f => f.Concept == concept && f.PeriodEnd == periodEnd);

    private static FundamentalsChainService CreateEdgarChain(SecEdgarService edgar)
        => new(
            [
                new SecEdgarFundamentalsSource(edgar, FundamentalFact.UsGaapTaxonomy),
                new SecEdgarFundamentalsSource(edgar, FundamentalFact.IfrsTaxonomy),
            ],
            Options.Create(new FundamentalsOptions()),
            new FixedTimeProvider(CaptureDate),
            NullLogger<FundamentalsChainService>.Instance);

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

    // The fact fields that existed before #837 added Taxonomy and provenance, as the pre-20-F snapshot recorded them.
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
