namespace FinanceSentry.Modules.Research.Tests.Screens;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Application.Services.Screens;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FinanceSentry.Modules.Research.Domain.Screens;
using FinanceSentry.Modules.Research.Tests.Opportunity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>The three screens over fixture data: what each looks at, how it is bounded, and the tag and evidence date its hits carry.</summary>
public sealed class ScreenSourceTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 11, 13, 23, 30, 0, TimeSpan.Zero);

    private static ScreenOptions NoPause()
    {
        var o = new ScreenOptions();
        o.Fundamentals.PauseBetweenTickersMs = 0;
        return o;
    }

    /// <summary>A reference universe with the given lists; each list is a key and its tickers.</summary>
    private static ScreenUniverse UniverseOf(ScreenOptions options, params (string Key, string[] Tickers)[] lists)
    {
        var reader = new Mock<IReferenceUniverseReader>();
        reader.Setup(r => r.GetListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(lists.Select(l => new ReferenceListInfo(l.Key, "index", "test", l.Tickers.Length)).ToList());
        foreach (var (key, tickers) in lists)
        {
            reader.Setup(r => r.GetMembersAsync(key, It.IsAny<CancellationToken>()))
                .ReturnsAsync(tickers.Select(t => new ReferenceMemberInfo(t, null)).ToList());
        }

        return new ScreenUniverse(reader.Object, Options.Create(options));
    }

    // ── the universe ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Universe_IsTheDistinctOrderedUnionOfTheConfiguredLists_WithAPrefixWildcard()
    {
        var options = new ScreenOptions { ListKeys = ["sp500", "theme:*", "nope"] };
        var universe = UniverseOf(options,
            ("sp500", ["MU", "brk.b"]), ("nasdaq100", ["AAPL"]), ("theme:memory", ["SNDK", "MU"]), ("theme:power", ["VRT"]));

        (await universe.TickersAsync()).Should().Equal("BRK-B", "MU", "SNDK", "VRT");
    }

    // ── fundamentals ──────────────────────────────────────────────────────────

    private static IReadOnlyList<FundamentalFact> PassingFacts(string ticker, decimal revenueNow = 1200m)
    {
        FundamentalFact Fy(string concept, decimal value, DateOnly end) => new(ticker, concept, concept, "USD", value, end, "FY", end.Year, "10-K");
        var end = new DateOnly(2026, 3, 31);
        var prior = new DateOnly(2025, 3, 31);
        return
        [
            Fy("Revenue", revenueNow, end), Fy("Revenue", 1000m, prior),
            Fy("OperatingCashFlow", 360m, end), Fy("Capex", 60m, end), Fy("StockholdersEquity", 500m, end),
        ];
    }

    private static FundamentalsScreen FundamentalsFor(
        ScreenOptions options, string[] tickers, RecordingFundamentalsService fundamentals)
        => new(UniverseOf(options, ("sp500", tickers)), fundamentals, Options.Create(options), NullLogger<FundamentalsScreen>.Instance);

    [Fact]
    public async Task Fundamentals_HitsCarryTheTag_TheFilingEvidenceDate_AndComeBestGrowthFirst()
    {
        var options = NoPause();
        var facts = new Dictionary<string, IReadOnlyList<FundamentalFact>>(StringComparer.OrdinalIgnoreCase)
        {
            ["SLOW"] = PassingFacts("SLOW", 1180m),
            ["FAST"] = PassingFacts("FAST", 1500m),
            ["FLAT"] = PassingFacts("FLAT", 1000m),
        };
        var fundamentals = new RecordingFundamentalsService(facts);

        var run = await FundamentalsFor(options, ["FAST", "FLAT", "SLOW"], fundamentals).RunAsync(AsOf);

        run.Tag.Should().Be(ScreenTags.Fundamentals);
        run.Hits.Select(h => h.Ticker).Should().Equal("FAST", "SLOW");
        run.Hits.Should().OnlyContain(h => h.Tag == ScreenTags.Fundamentals && h.SupportingReasons.Count == 0);
        // Fiscal year ended 2026-03-31 + the 75-day filing lag.
        run.Hits[0].EvidenceAt.Should().Be(new DateTimeOffset(2026, 6, 14, 0, 0, 0, TimeSpan.Zero));
        run.Hits[0].Detail.Should().Contain("2026-03-31").And.Contain("revenue growth 50%");
        run.Examined.Should().Be(3);
    }

    [Fact]
    public async Task Fundamentals_ReadsAtMostABatchPerRun_AndRotatesSoEveryNameIsReadOverTheDays()
    {
        var options = NoPause();
        options.Fundamentals.BatchSize = 4;
        var tickers = Enumerable.Range(1, 10).Select(i => $"T{i:00}").ToArray();
        var fundamentals = new RecordingFundamentalsService(new Dictionary<string, IReadOnlyList<FundamentalFact>>());
        var screen = FundamentalsFor(options, tickers, fundamentals);

        for (var day = 0; day < 3; day++)
        {
            fundamentals.FundamentalsRequests.Clear();
            await screen.RunAsync(AsOf.AddDays(day));
            fundamentals.FundamentalsRequests.Should().HaveCountLessThanOrEqualTo(4);
        }

        var seen = new HashSet<string>();
        for (var day = 0; day < 3; day++)
        {
            fundamentals.FundamentalsRequests.Clear();
            await screen.RunAsync(AsOf.AddDays(day));
            seen.UnionWith(fundamentals.FundamentalsRequests);
        }

        seen.Should().BeEquivalentTo(tickers, "three nightly slices of four cover ten names");
    }

    [Fact]
    public async Task Fundamentals_ASameDayRerun_ReadsTheSameSliceAndFindsTheSameHits()
    {
        var options = NoPause();
        options.Fundamentals.BatchSize = 2;
        var facts = new Dictionary<string, IReadOnlyList<FundamentalFact>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = PassingFacts("A"), ["B"] = PassingFacts("B"), ["C"] = PassingFacts("C"), ["D"] = PassingFacts("D"),
        };
        var screen = FundamentalsFor(options, ["A", "B", "C", "D"], new RecordingFundamentalsService(facts));

        var first = await screen.RunAsync(AsOf);
        var second = await screen.RunAsync(AsOf.AddMinutes(10));

        second.Hits.Select(h => h.Ticker).Should().Equal(first.Hits.Select(h => h.Ticker));
        first.Hits.Should().HaveCount(2);
    }

    [Fact]
    public async Task Fundamentals_OneNameFailing_IsSkipped_ButEveryNameFailingFailsTheScreen()
    {
        var options = NoPause();
        var facts = new Dictionary<string, IReadOnlyList<FundamentalFact>>(StringComparer.OrdinalIgnoreCase) { ["OK"] = PassingFacts("OK") };

        var partial = await FundamentalsFor(options, ["BAD", "OK"], new RecordingFundamentalsService(facts, ["BAD"])).RunAsync(AsOf);
        partial.Hits.Select(h => h.Ticker).Should().Equal("OK");

        var allDown = () => FundamentalsFor(options, ["BAD", "WORSE"], new RecordingFundamentalsService(facts, ["BAD", "WORSE"])).RunAsync(AsOf);
        await allDown.Should().ThrowAsync<InvalidOperationException>().WithMessage("*EDGAR*");
    }

    // ── relative strength ─────────────────────────────────────────────────────

    private static UniverseStructureEntry Strength(string ticker, decimal rs, bool atHigh = false)
        => new(
            ticker,
            IsEtfLens: false,
            new MarketStructureSnapshot(
                ticker,
                new Dictionary<int, decimal?> { [ScanNominationRules.RsWindowBars] = rs },
                new Dictionary<int, decimal?>(),
                ExtensionFromMa50: 0.05m, TodayZScore: 0m, VolumeRatio: 1.0m, Ma50: 50m, Ma200: 48m, Stale: false,
                DistanceFrom63dHigh: atHigh ? 0m : -0.2m));

    private static RelativeStrengthScreen RsFor(
        ScreenOptions options, string[] reference, IReadOnlyList<UniverseStructureEntry> bars)
        => new(UniverseOf(options, ("sp500", reference)), new FakeUniverseStructureReader(bars), Options.Create(options), Options.Create(new OpportunityOptions()));

    [Fact]
    public async Task RelativeStrength_RanksOnlyReferenceListNames_SoABookOnlyNameDoesNotMoveTheirPercentile()
    {
        var options = new ScreenOptions();
        options.RelativeStrength.MinPoolSize = 5;
        var reference = Enumerable.Range(1, 10).Select(i => $"R{i:00}").ToArray();
        var bars = reference.Select((t, i) => Strength(t, 0.01m * (i + 1))).ToList();
        // A held name far stronger than anything in the pool would, if ranked, push R09 (90th percentile of the ten
        // reference names, 82nd of eleven) out of the top decile.
        bars.Add(Strength("HELD", 5m));

        var run = await RsFor(options, reference, bars).RunAsync(AsOf);

        run.Examined.Should().Be(10);
        run.Hits.Select(h => h.Ticker).Should().Equal("R10", "R09");
        run.Hits[0].Tag.Should().Be(ScreenTags.RelativeStrength);
        run.Hits[0].Reasons.Should().Equal(ScreenTags.RelativeStrength, ScanNominationRules.TopDecileRsReason);
        run.Hits[0].EvidenceAt.Should().Be(AsOf);
        run.Hits[0].Detail.Should().Contain("percentile 100").And.Contain("10 reference-list names");
    }

    [Fact]
    public async Task RelativeStrength_ABreakoutAmongTheReferenceNames_IsFoundWithItsRuleReason()
    {
        var options = new ScreenOptions();
        options.RelativeStrength.MinPoolSize = 3;
        var bars = new List<UniverseStructureEntry> { Strength("A", 0.01m), Strength("B", 0.02m) };
        bars.Add(Strength("C", 0.03m, atHigh: true) with { Snapshot = Strength("C", 0.03m, atHigh: true).Snapshot with { VolumeRatio = 2m } });

        var run = await RsFor(options, ["A", "B", "C"], bars).RunAsync(AsOf);

        run.Hits.Single(h => h.Ticker == "C").Reasons.Should().Contain(ScanNominationRules.BreakoutReason);
    }

    [Fact]
    public async Task RelativeStrength_AReferencePoolBelowTheMinimum_NominatesNothing_AndSaysWhy()
    {
        var options = new ScreenOptions();
        options.RelativeStrength.MinPoolSize = 20;
        var reference = new[] { "A", "B", "C" };

        var run = await RsFor(options, reference, reference.Select((t, i) => Strength(t, 0.1m * (i + 1))).ToList()).RunAsync(AsOf);

        run.Hits.Should().BeEmpty();
        run.Note.Should().Contain("pool too small").And.Contain("3").And.Contain("20");
    }

    // ── dated catalyst ────────────────────────────────────────────────────────

    private sealed class FakeEarningsWindow(bool configured, params EarningsCalendarItem[] items) : IEarningsWindowSource
    {
        public bool IsConfigured => configured;

        public List<(DateOnly From, DateOnly To)> Windows { get; } = [];

        public Task<IReadOnlyList<EarningsCalendarItem>> GetWindowAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            Windows.Add((from, to));
            return Task.FromResult<IReadOnlyList<EarningsCalendarItem>>(items);
        }
    }

    [Fact]
    public async Task Catalyst_WithoutAnApiKey_IsSkippedQuietly_AndMakesNoRequest()
    {
        var options = new ScreenOptions();
        var calendar = new FakeEarningsWindow(false);

        var run = await new CatalystScreen(UniverseOf(options, ("sp500", ["MU"])), calendar, Options.Create(options)).RunAsync(AsOf);

        run.Hits.Should().BeEmpty();
        run.Note.Should().Contain("Finnhub");
        calendar.Windows.Should().BeEmpty();
    }

    [Fact]
    public async Task Catalyst_AsksForTheWindowOnce_KeepsReferenceNames_AndStampsWhenTheDateEnteredTheWindow()
    {
        var options = new ScreenOptions();
        options.Catalyst.LookaheadDays = 10;
        var calendar = new FakeEarningsWindow(
            true,
            new EarningsCalendarItem("MU", "earnings", new DateOnly(2026, 11, 20), false, "finnhub"),
            new EarningsCalendarItem("XYZ", "earnings", new DateOnly(2026, 11, 15), false, "finnhub"));

        var run = await new CatalystScreen(UniverseOf(options, ("sp500", ["MU"])), calendar, Options.Create(options)).RunAsync(AsOf);

        calendar.Windows.Should().Equal((new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 23)));
        var hit = run.Hits.Should().ContainSingle().Which;
        hit.Ticker.Should().Be("MU");
        hit.Tag.Should().Be(ScreenTags.Catalyst);
        hit.Detail.Should().Contain("2026-11-20").And.Contain("7 day(s)");
        // The date entered the 10-day window on 2026-11-10.
        hit.EvidenceAt.Should().Be(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero));
        run.Examined.Should().Be(2);
    }

    [Fact]
    public async Task Catalyst_AProviderOutage_FailsTheScreen_NotReadAsNothingReporting()
    {
        var options = new ScreenOptions();
        var calendar = new Mock<IEarningsWindowSource>();
        calendar.SetupGet(c => c.IsConfigured).Returns(true);
        calendar.Setup(c => c.GetWindowAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EarningsCalendarProviderException("down"));

        var act = () => new CatalystScreen(UniverseOf(options, ("sp500", ["MU"])), calendar.Object, Options.Create(options)).RunAsync(AsOf);

        await act.Should().ThrowAsync<EarningsCalendarProviderException>();
    }
}
