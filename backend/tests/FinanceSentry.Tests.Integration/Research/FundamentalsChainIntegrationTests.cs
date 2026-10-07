namespace FinanceSentry.Tests.Integration.Research;

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.ThesisMonitor;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// #837 acceptance, end to end through the module's real registration (<c>AddFundamentalsChain</c>):
/// the real <see cref="SecEdgarService"/>, both EDGAR taxonomy sources and the Yahoo source, with only
/// the HTTP edge replaced by recorded provider responses (captured 2026-10-07; anything not recorded
/// answers as the provider does for an unknown tag or symbol). Covers (a) a domestic filer unchanged,
/// (b) a foreign private issuer resolved via fallback, (c) a ticker no source covers, and the thesis
/// monitor on both.
/// </summary>
public sealed class FundamentalsChainIntegrationTests
{
    private static readonly DateTimeOffset CaptureDate = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "Research", "Fixtures", "Fundamentals");

    // Yahoo's real answer for a symbol it has no fundamentals for: every series present, no points.
    private const string YahooEmptyAnswer =
        """{"timeseries":{"result":[{"meta":{"symbol":["NOSUCHTKR"],"type":["annualTotalRevenue"]}}],"error":null}}""";

    private readonly ConcurrentBag<Uri> requests = [];

    [Fact]
    public async Task DomesticFiler_IsServedByEdgarUsGaapAlone_Unchanged()
    {
        var handler = new GetFundamentalsQueryHandler(BuildChain());

        var result = await handler.Handle(new GetFundamentalsQuery("DUOL", 20), CancellationToken.None);

        result.Coverage.Status.Should().Be(FundamentalsCoverageStatus.Covered);
        result.Coverage.IssuerType.Should().Be(FundamentalsIssuerType.DomesticFiler);
        result.Coverage.Bases.Should().OnlyContain(b => b.Provider == FundamentalsSourceNames.SecEdgarUsGaap && !b.Stale);
        result.Facts.Should().NotBeEmpty();
        result.Facts.Should().OnlyContain(f => f.Taxonomy == FundamentalFact.UsGaapTaxonomy
            && f.SourceProvenance!.Provider == FundamentalsSourceNames.SecEdgarUsGaap);
        requests.Should().NotContain(u => u.AbsolutePath.Contains("/ifrs-full/", StringComparison.Ordinal),
            "a filer EDGAR us-gaap covers fresh on both bases never reaches another source");
        requests.Should().NotContain(u => u.Host.EndsWith("yahoo.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForeignPrivateIssuer_Grab_IsResolvedViaTheChain_WithProvenanceOnEveryFact()
    {
        var handler = new GetFundamentalsQueryHandler(BuildChain());

        var result = await handler.Handle(new GetFundamentalsQuery("GRAB", 8), CancellationToken.None);

        result.Coverage.Status.Should().Be(FundamentalsCoverageStatus.Covered);
        result.Coverage.IssuerType.Should().Be(FundamentalsIssuerType.ForeignPrivateIssuer);
        result.Coverage.Bases.Select(b => (b.Basis, b.Provider)).Should().BeEquivalentTo(
        [
            (FundamentalsBasis.Quarterly, FundamentalsSourceNames.YahooFinance),
            (FundamentalsBasis.Annual, FundamentalsSourceNames.SecEdgarIfrs),
        ]);

        // >= 4 recent periods of Revenue and OperatingIncome, every one with provenance.
        foreach (var concept in new[] { "Revenue", "OperatingIncome" })
        {
            var series = result.Facts.Where(f => f.Concept == concept).ToList();
            series.Should().HaveCountGreaterThanOrEqualTo(4);
            series.Where(f => f.FiscalPeriod == "3M").Should().HaveCount(5);
            series.Max(f => f.PeriodEnd).Should().Be(new DateOnly(2026, 6, 30));
        }

        result.Facts.Should().OnlyContain(f => f.SourceProvenance != null && f.SourceProvenance.DocumentUrl != null);
        result.Facts.Where(f => f.FiscalPeriod == "FY").Should().OnlyContain(f =>
            f.SourceProvenance!.Provider == FundamentalsSourceNames.SecEdgarIfrs && f.Form == "20-F");
        result.Facts.Where(f => f.FiscalPeriod != "FY").Should().OnlyContain(f =>
            f.SourceProvenance!.Provider == FundamentalsSourceNames.YahooFinance);
    }

    [Fact]
    public async Task UncoveredTicker_ReturnsTheExplicitFlag_NotABareEmptyList()
    {
        var handler = new GetFundamentalsQueryHandler(BuildChain());

        var result = await handler.Handle(new GetFundamentalsQuery("NOSUCHTKR", 5), CancellationToken.None);

        result.Facts.Should().BeEmpty();
        result.Coverage.Status.Should().Be(FundamentalsCoverageStatus.NoSourceAvailable);
        result.Coverage.IssuerType.Should().Be(FundamentalsIssuerType.NotSecRegistrant);
        result.Coverage.Reason.Should().Contain(FundamentalsSourceNames.SecEdgarUsGaap)
            .And.Contain(FundamentalsSourceNames.YahooFinance);
    }

    [Fact]
    public async Task ThesisMonitor_EvaluatesGrabOnTheChainsQuarters_AndNamesWhatItCannotSee()
    {
        // GRAB's thesis triggers: operating margin reversing to a loss, and revenue YoY under 10%,
        // each for two consecutive quarters.
        var margin = new ThesisInvalidationTrigger(ThesisMetric.OperatingMargin, "lessThan", 0m, ConsecutivePeriods: 2);
        var growth = new ThesisInvalidationTrigger(ThesisMetric.RevenueYoy, "lessThan", 0.10m, ConsecutivePeriods: 2);
        var grab = Thesis("GRAB", margin, growth);
        var uncovered = Thesis("NOSUCHTKR", margin);

        var summary = await RunMonitorAsync(grab, uncovered);

        // Operating margin is evaluated on Yahoo's five quarters (positive both times: held, no break);
        // a two-quarter revenue YoY needs six, so it is reported blind with the coverage behind it.
        summary.BreaksRaised.Should().Be(0);
        var grabBlind = summary.Unmonitorable.Single(u => u.ThesisId == grab.Id);
        grabBlind.Status.Should().Be(ThesisMonitorability.PartiallyMonitorable);
        grabBlind.Triggers.Should().ContainSingle().Which.Should().Match<UnmonitorableTrigger>(t =>
            t.Metric == ThesisMetric.RevenueYoy
            && t.Reason == NonEvaluableReason.InsufficientPeriods
            && t.Coverage!.Bases.Any(b => b.Basis == FundamentalsBasis.Quarterly && b.Provider == FundamentalsSourceNames.YahooFinance));

        var uncoveredBlind = summary.Unmonitorable.Single(u => u.ThesisId == uncovered.Id);
        uncoveredBlind.Status.Should().Be(ThesisMonitorability.Unmonitorable);
        uncoveredBlind.Triggers.Single().Coverage!.Status.Should().Be(FundamentalsCoverageStatus.NoSourceAvailable);
    }

    private async Task<ThesisMonitorRunSummary> RunMonitorAsync(params InvestmentThesis[] theses)
    {
        var repo = new Mock<IThesisRepository>();
        repo.Setup(r => r.ListUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(theses);
        var handler = new RunThesisMonitorCommandHandler(
            repo.Object,
            BuildChain(),
            Mock.Of<IMarketDataService>(),
            Mock.Of<IAlertGeneratorService>(),
            Mock.Of<IThesisEventRecorder>(),
            NullLogger<RunThesisMonitorCommandHandler>.Instance);
        return await handler.Handle(new RunThesisMonitorCommand(Guid.NewGuid()), CancellationToken.None);
    }

    private static InvestmentThesis Thesis(string ticker, params ThesisInvalidationTrigger[] triggers) => new()
    {
        Ticker = ticker,
        ThesisText = $"{ticker} thesis",
        InvalidationTriggers = [.. triggers],
    };

    private IFundamentalsService BuildChain()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(CaptureDate));
        services.AddSingleton<ISecEdgarService, SecEdgarService>();
        services.AddFundamentalsChain(new ConfigurationBuilder().Build());

        // Only the HTTP edge is replaced: the last primary handler configured for a named client wins.
        services.AddHttpClient(SecEdgarService.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new FixtureHandler(Respond));
        services.AddHttpClient(YahooFundamentalsSource.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new FixtureHandler(Respond));

        return services.BuildServiceProvider().GetRequiredService<IFundamentalsService>();
    }

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        requests.Add(uri);

        if (uri.Host.EndsWith("yahoo.com", StringComparison.Ordinal))
        {
            var symbol = uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf('/') + 1)..];
            var yahooFile = Path.Combine(FixtureDir, "Yahoo", $"{symbol}-fundamentals-timeseries.json");
            return Json(File.Exists(yahooFile) ? File.ReadAllText(yahooFile) : YahooEmptyAnswer);
        }

        string? file = null;
        if (uri.AbsolutePath.EndsWith("/company_tickers.json", StringComparison.Ordinal))
        {
            file = "company_tickers-subset.json";
        }
        else if (uri.AbsolutePath.StartsWith("/api/xbrl/companyconcept/", StringComparison.Ordinal))
        {
            file = uri.AbsolutePath["/api/xbrl/companyconcept/".Length..].Replace('/', '-');
        }

        var path = file is null ? null : Path.Combine(FixtureDir, "Edgar", file);
        return path is not null && File.Exists(path)
            ? Json(File.ReadAllText(path))
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
