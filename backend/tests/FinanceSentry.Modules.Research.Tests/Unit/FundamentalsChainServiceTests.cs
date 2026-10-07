namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Fundamentals;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// #837: the ordered provider chain's rules, over scripted sources — per-basis fill from the first
/// fresh source, freshness fall-through, a failure halting the fall-through, configurable order, and
/// the explicit coverage answer in place of a bare empty list.
/// </summary>
public sealed class FundamentalsChainServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly RecentQuarter = new(2026, 6, 30);
    private static readonly DateOnly RecentYear = new(2025, 12, 31);
    private static readonly DateOnly OldQuarter = new(2023, 6, 30);
    private static readonly DateOnly OldYear = new(2022, 12, 31);

    [Fact]
    public async Task FirstSourceCoveringBothBases_IsTheOnlyOneAsked()
    {
        var first = new ScriptedSource("first", Facts("first", Quarter(RecentQuarter), Year(RecentYear)));
        var second = new ScriptedSource("second", Facts("second", Quarter(RecentQuarter)));

        var result = await Chain([first, second]).GetFundamentalsAsync("AAPL", 20);

        second.Calls.Should().Be(0);
        result.Facts.Should().OnlyContain(f => f.SourceProvenance!.Provider == "first");
        result.Coverage.Status.Should().Be(FundamentalsCoverageStatus.Covered);
        result.Coverage.Reason.Should().BeNull();
        result.Coverage.Bases.Should().BeEquivalentTo(
        [
            new FundamentalsBasisCoverage(FundamentalsBasis.Quarterly, "first", RecentQuarter, false),
            new FundamentalsBasisCoverage(FundamentalsBasis.Annual, "first", RecentYear, false),
        ]);
    }

    [Fact]
    public async Task MissingBasis_IsFilledWholeByTheNextSource_NeverMixedWithinABasis()
    {
        // GRAB's shape: EDGAR has fiscal years only; the quarters come from the next source.
        var edgar = new ScriptedSource("edgar", Facts("edgar", Year(RecentYear), Year(new DateOnly(2024, 12, 31))));
        var yahoo = new ScriptedSource("yahoo", Facts("yahoo",
            Quarter(RecentQuarter), Quarter(new DateOnly(2026, 3, 31)), Year(RecentYear)));

        var result = await Chain([edgar, yahoo]).GetFundamentalsAsync("GRAB", 20);

        result.Facts.Where(f => f.FiscalPeriod == "FY").Should().OnlyContain(f => f.SourceProvenance!.Provider == "edgar");
        result.Facts.Where(f => f.FiscalPeriod != "FY").Should().OnlyContain(f => f.SourceProvenance!.Provider == "yahoo");
        result.Facts.Should().HaveCount(4, "yahoo's own annual row is not used: edgar already supplies the annual basis");
        result.Coverage.Bases.Select(b => (b.Basis, b.Provider)).Should().BeEquivalentTo(
            [(FundamentalsBasis.Quarterly, "yahoo"), (FundamentalsBasis.Annual, "edgar")]);
    }

    [Fact]
    public async Task StaleSeries_FallsThroughToAFresherSource()
    {
        // A filer that moved from us-gaap 10-Ks to IFRS 20-Fs: old us-gaap history must not hide the IFRS years.
        var usGaap = new ScriptedSource("us-gaap", Facts("us-gaap", Quarter(OldQuarter), Year(OldYear)));
        var ifrs = new ScriptedSource("ifrs", Facts("ifrs", Year(RecentYear)));

        var result = await Chain([usGaap, ifrs]).GetFundamentalsAsync("XYZ", 20);

        result.Coverage.Bases.Should().ContainEquivalentOf(
            new FundamentalsBasisCoverage(FundamentalsBasis.Annual, "ifrs", RecentYear, false));
        // Nobody has fresh quarters, so the stale ones are kept — and flagged.
        result.Coverage.Bases.Should().ContainEquivalentOf(
            new FundamentalsBasisCoverage(FundamentalsBasis.Quarterly, "us-gaap", OldQuarter, true));
    }

    [Fact]
    public async Task FailedSource_HaltsTheFallThrough()
    {
        var edgar = new ScriptedSource("edgar", FundamentalsSourceResult.Failed("503 from data.sec.gov"));
        var yahoo = new ScriptedSource("yahoo", Facts("yahoo", Quarter(RecentQuarter), Year(RecentYear)));

        var result = await Chain([edgar, yahoo]).GetFundamentalsAsync("AAPL", 20);

        yahoo.Calls.Should().Be(0, "an outage must not swap in another provider's series for this run");
        result.Facts.Should().BeEmpty();
        result.Coverage.Status.Should().Be(FundamentalsCoverageStatus.SourceUnavailable);
        result.Coverage.Reason.Should().Contain("edgar unavailable").And.Contain("503");
    }

    [Fact]
    public async Task FailureAfterAPartialAnswer_KeepsWhatWasSupplied_AndSaysWhatIsMissing()
    {
        var edgar = new ScriptedSource("edgar", Facts("edgar", Year(RecentYear)));
        var yahoo = new ScriptedSource("yahoo", FundamentalsSourceResult.Failed("429"));

        var result = await Chain([edgar, yahoo]).GetFundamentalsAsync("GRAB", 20);

        result.Coverage.Status.Should().Be(FundamentalsCoverageStatus.Covered);
        result.Coverage.Reason.Should().Contain("no source has quarterly periods").And.Contain("yahoo unavailable");
        result.Facts.Should().ContainSingle();
    }

    [Fact]
    public async Task ThrowingSource_IsTreatedAsFailed()
    {
        var broken = new ScriptedSource("broken", () => throw new HttpRequestException("socket closed"));

        var result = await Chain([broken]).GetFundamentalsAsync("AAPL", 20);

        result.Coverage.Status.Should().Be(FundamentalsCoverageStatus.SourceUnavailable);
    }

    [Fact]
    public async Task NoSourceHasAnything_ReturnsTheExplicitFlag_NotABareEmptyList()
    {
        var edgar = new ScriptedSource("edgar", FundamentalsSourceResult.NoData(
            "not in the SEC ticker map", FundamentalsIssuerType.NotSecRegistrant));
        var yahoo = new ScriptedSource("yahoo", FundamentalsSourceResult.NoData("no fundamentals timeseries"));

        var result = await Chain([edgar, yahoo]).GetFundamentalsAsync("EVVTY", 20);

        result.Facts.Should().BeEmpty();
        result.Coverage.Status.Should().Be(FundamentalsCoverageStatus.NoSourceAvailable);
        result.Coverage.IssuerType.Should().Be(FundamentalsIssuerType.NotSecRegistrant);
        result.Coverage.Reason.Should().Contain("edgar: not in the SEC ticker map").And.Contain("yahoo: no fundamentals timeseries");
    }

    [Fact]
    public async Task InvestmentFund_IsAnUnsupportedIssuerType()
    {
        var edgar = new ScriptedSource("edgar", FundamentalsSourceResult.NoData(
            "no us-gaap facts filed", FundamentalsIssuerType.InvestmentFund));

        var result = await Chain([edgar]).GetFundamentalsAsync("TLT", 20);

        result.Coverage.Status.Should().Be(FundamentalsCoverageStatus.UnsupportedIssuerType);
        result.Coverage.IssuerType.Should().Be(FundamentalsIssuerType.InvestmentFund);
    }

    [Fact]
    public async Task InvestmentFund_StaysUnsupported_WhenALaterSourceFails()
    {
        var edgar = new ScriptedSource("edgar", FundamentalsSourceResult.NoData(
            "no us-gaap facts filed", FundamentalsIssuerType.InvestmentFund));
        var yahoo = new ScriptedSource("yahoo", FundamentalsSourceResult.Failed("429"));

        var result = await Chain([edgar, yahoo]).GetFundamentalsAsync("TLT", 20);

        result.Coverage.Status.Should().Be(FundamentalsCoverageStatus.UnsupportedIssuerType);
        result.Coverage.IssuerType.Should().Be(FundamentalsIssuerType.InvestmentFund);
    }

    [Fact]
    public async Task ConfiguredOrder_DecidesWhoIsAskedFirst_AndLeavesOutUnlistedSources()
    {
        var a = new ScriptedSource("a", Facts("a", Quarter(RecentQuarter), Year(RecentYear)));
        var b = new ScriptedSource("b", Facts("b", Quarter(RecentQuarter), Year(RecentYear)));
        var c = new ScriptedSource("c", Facts("c", Quarter(RecentQuarter), Year(RecentYear)));

        var result = await Chain([a, b, c], order: ["b", "nope", "a"]).GetFundamentalsAsync("AAPL", 20);

        result.Facts.Should().OnlyContain(f => f.SourceProvenance!.Provider == "b");
        a.Calls.Should().Be(0);
        c.Calls.Should().Be(0);
    }

    [Fact]
    public void DefaultOrder_IsEdgarUsGaap_ThenIfrs_ThenYahoo()
        => FundamentalsOptions.DefaultSourceOrder.Should().Equal(
            FundamentalsSourceNames.SecEdgarUsGaap, FundamentalsSourceNames.SecEdgarIfrs, FundamentalsSourceNames.YahooFinance);

    [Fact]
    public async Task Facts_AreTrimmedPerConcept_NewestFirst()
    {
        var source = new ScriptedSource("s", Facts("s",
            Quarter(RecentQuarter), Quarter(new DateOnly(2026, 3, 31)), Quarter(new DateOnly(2025, 12, 31)), Year(RecentYear)));

        var result = await Chain([source]).GetFundamentalsAsync("AAPL", 2);

        result.Facts.Select(f => f.PeriodEnd).Should().Equal(RecentQuarter, new DateOnly(2026, 3, 31));
    }

    private static FundamentalsChainService Chain(IEnumerable<IFundamentalsSource> sources, string[]? order = null)
    {
        var list = sources.ToList();
        var options = new FundamentalsOptions { SourceOrder = order ?? list.Select(s => s.Name).ToArray() };
        return new FundamentalsChainService(
            list, Options.Create(options), new FixedTimeProvider(Now), NullLogger<FundamentalsChainService>.Instance);
    }

    private static (DateOnly End, string Period) Quarter(DateOnly end) => (end, "3M");

    private static (DateOnly End, string Period) Year(DateOnly end) => (end, "FY");

    private static FundamentalsSourceResult Facts(string provider, params (DateOnly End, string Period)[] periods)
        => FundamentalsSourceResult.WithFacts(periods
            .Select(p => new FundamentalFact(
                "T", "Revenue", "Revenue", "USD", 100m, p.End, p.Period, null, string.Empty, "test",
                new SourceProvenance(provider, null, Now)))
            .ToList());

    private sealed class ScriptedSource(string name, Func<FundamentalsSourceResult> answer) : IFundamentalsSource
    {
        public ScriptedSource(string name, FundamentalsSourceResult result)
            : this(name, () => result)
        {
        }

        public int Calls { get; private set; }

        public string Name => name;

        public Task<FundamentalsSourceResult> FetchAsync(string ticker, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(answer());
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
