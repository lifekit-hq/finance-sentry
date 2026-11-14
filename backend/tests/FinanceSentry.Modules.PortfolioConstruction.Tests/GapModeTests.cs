namespace FinanceSentry.Modules.PortfolioConstruction.Tests;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.PortfolioConstruction.API.Responses;
using FinanceSentry.Modules.PortfolioConstruction.Application.Queries;
using FinanceSentry.Modules.PortfolioConstruction.Application.Services;
using FinanceSentry.Modules.PortfolioConstruction.Domain.Ports;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

/// <summary>
/// Gap mode (spec 726 US4) end to end through the real universe handler: coverage AAA, BBB, CCC under the growth
/// defaults build 12% each (the 12% position cap bites on a three-name book), so the target is Equities 36% and
/// Cash 64% of the whole book.
/// </summary>
public sealed class GapModeTests
{
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUniverseScorecardSource> _scorecards = new();
    private readonly Mock<IConstructionPolicySource> _policy = new();
    private readonly Mock<IHeldEquitySource> _held = new();
    private readonly Mock<IWatchlistReader> _watchlist = new();
    private readonly Mock<ICoverageUniverseReader> _coverage = new();
    private readonly Mock<IBookFiguresService> _book = new();
    private readonly Dictionary<string, UniverseScorecard> _cards = [];

    public GapModeTests()
    {
        _policy.Setup(p => p.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConstructionPolicy.None);
        _held.Setup(h => h.ListSymbolsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _watchlist.Setup(w => w.ListTickersAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _coverage.Setup(c => c.ListTickersAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(["AAA", "BBB", "CCC"]);
        _scorecards
            .Setup(s => s.GetAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<string> symbols, CancellationToken _) =>
                Task.FromResult<IReadOnlyList<UniverseScorecard>>(
                    [.. symbols.Select(s => _cards.GetValueOrDefault(s) ?? Cards.Scored(s))]));
        Book(BookFigures.Empty);
    }

    private void Book(BookFigures book) => _book.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(book);

    private IQueryHandler<GetUniverseScreenQuery, UniverseScreenResult> UniverseHandler() =>
        new GetUniverseScreenQueryHandler(
            _scorecards.Object, _policy.Object, _held.Object, _watchlist.Object, _coverage.Object,
            new MemoryCache(new MemoryCacheOptions()), Options.Create(new PortfolioConstructionOptions()), new FakeTimeProvider(Now));

    private Task<GapModeResult> Gap(string? archetype = null) =>
        new GetGapModeQueryHandler(UniverseHandler(), _book.Object, new FakeTimeProvider(Now))
            .Handle(new GetGapModeQuery(UserId, archetype), CancellationToken.None);

    private static BookFigurePosition Pos(string symbol, decimal usd, string assetClass = AssetClassNormalizer.Equities) =>
        new(symbol, assetClass, 1m, null, usd, "ibkr");

    /// <summary>A book whose total is the cash plus the positions, the way the canonical figures add up.</summary>
    private static BookFigures BookOf(decimal cash, params BookFigurePosition[] positions)
    {
        var invested = positions.Sum(p => p.UsdValue);
        return new BookFigures(cash, 0m, cash, invested, cash + invested, positions, false, []);
    }

    [Fact]
    public async Task EmptyBook_ShowsEveryTargetNameAsMissing_WithPercentagesOnlyAndNoAmounts()
    {
        var result = await Gap();

        result.HasBook.Should().BeFalse();
        result.BookValueUsd.Should().Be(0m);
        result.Names.Select(n => n.Symbol).Should().Equal("AAA", "BBB", "CCC");
        result.Names.Should().OnlyContain(n =>
            n.Status == GapStatus.Missing && n.CurrentPct == 0m && n.CurrentUsd == 0m && n.TargetPct == 12m && n.GapPct == 12m
            && n.TargetUsd == null && n.GapUsd == null && !n.HeldOutsideTarget);
        result.Summary.MissingNames.Should().Equal("AAA", "BBB", "CCC");
        result.Summary.OverweightNames.Should().BeEmpty();
        result.Summary.Headline.Should().StartWith("Missing: AAA, BBB, CCC.");
        result.Notes.Should().Contain(n => n.Contains("no value to size against"));
        result.Sleeves.Single(s => s.AssetClass == "Equities").TargetPct.Should().Be(36m);
        result.Sleeves.Single(s => s.AssetClass == "Cash").TargetPct.Should().Be(64m);
        result.Sleeves.Should().OnlyContain(s => s.TargetUsd == null && s.GapUsd == null);
    }

    [Fact]
    public async Task FullyMatchedBook_HasNoGapAnywhere()
    {
        Book(BookOf(6400m, Pos("AAA", 1200m), Pos("BBB", 1200m), Pos("CCC", 1200m)));

        var result = await Gap();

        result.HasBook.Should().BeTrue();
        result.BookValueUsd.Should().Be(10000m);
        result.Names.Should().OnlyContain(n =>
            n.Status == GapStatus.OnTarget && n.GapPct == 0m && n.GapUsd == 0m && n.CurrentPct == 12m && n.TargetUsd == 1200m);
        result.Sleeves.Should().OnlyContain(s => s.Status == GapStatus.OnTarget && s.GapPct == 0m && s.GapUsd == 0m);
        result.Summary.Headline.Should().Be("The book matches the constructed target.");
        result.Summary.OnTargetCount.Should().Be(3);
        result.Summary.MissingNames.Should().BeEmpty();
        result.Summary.OverweightNames.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingUnderweightAndOverweightNames_AreTaggedWithAmounts_AndOrderedByGap()
    {
        _cards["AAA"] = Cards.Scored("AAA", sector: "Technology");
        _cards["BBB"] = Cards.Scored("BBB", sector: "Energy");
        _cards["CCC"] = Cards.Scored("CCC", sector: "Technology");
        // AAA 9% (under by 3), BBB not held, CCC 25% (over by 13); cash is what is left.
        Book(BookOf(6600m, Pos("AAA", 900m), Pos("CCC", 2500m)));

        var result = await Gap();

        var byName = result.Names.ToDictionary(n => n.Symbol);
        byName["BBB"].Should().Match<NameGapDto>(n =>
            n.Status == GapStatus.Missing && n.CurrentUsd == 0m && n.TargetUsd == 1200m && n.GapUsd == 1200m && n.Sector == "Energy");
        byName["AAA"].Should().Match<NameGapDto>(n =>
            n.Status == GapStatus.Underweight && n.CurrentPct == 9m && n.GapPct == 3m && n.GapUsd == 300m && n.Sector == "Technology");
        byName["CCC"].Should().Match<NameGapDto>(n =>
            n.Status == GapStatus.Overweight && n.CurrentPct == 25m && n.GapPct == -13m && n.GapUsd == -1300m && !n.HeldOutsideTarget);
        result.Names.Select(n => n.Symbol).Should().Equal("CCC", "BBB", "AAA");
        result.Summary.MissingNames.Should().Equal("BBB");
        result.Summary.UnderweightNames.Should().Equal("AAA");
        result.Summary.OverweightNames.Should().Equal("CCC");
        result.Summary.Headline.Should().Contain("Missing: BBB.").And.Contain("Overweight: CCC.");
        byName["BBB"].Reason.Should().Contain("Not held");
    }

    [Fact]
    public async Task HeldNamesOutsideTheTarget_AreOverweight_WithTheirSector_WhetherOrNotTheUniverseHoldsThem()
    {
        _cards["ZZZ"] = Cards.Scored("ZZZ", word: "Avoid", sector: "Utilities");
        _held.Setup(h => h.ListSymbolsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(["ZZZ"]);
        // ZZZ is in the universe but gets no weight (Avoid); VOO is an ETF the universe never reads.
        Book(BookOf(4900m, Pos("AAA", 1200m), Pos("BBB", 1200m), Pos("CCC", 1200m), Pos("ZZZ", 500m), Pos("VOO", 1000m)));

        var result = await Gap();

        var zzz = result.Names.Single(n => n.Symbol == "ZZZ");
        zzz.Should().Match<NameGapDto>(n =>
            n.Status == GapStatus.Overweight && n.HeldOutsideTarget && n.TargetPct == 0m && n.CurrentPct == 5m && n.GapPct == -5m
            && n.TargetUsd == 0m && n.GapUsd == -500m && n.Sector == "Utilities");
        zzz.Reason.Should().StartWith("Held outside the target").And.Contain("AVOID");
        zzz.Verdict.Should().Be("Avoid");
        var voo = result.Names.Single(n => n.Symbol == "VOO");
        voo.HeldOutsideTarget.Should().BeTrue();
        voo.Sector.Should().BeNull();
        result.Sectors.Single(s => s.Sector == "Unclassified").Symbols.Should().Contain("VOO");
        result.Sectors.Single(s => s.Sector == "Utilities").Should().Match<SectorGapDto>(s =>
            s.TargetPct == 0m && s.CurrentPct == 5m && s.Status == GapStatus.Overweight);
        result.Summary.OverweightNames.Should().BeEquivalentTo("ZZZ", "VOO");
    }

    [Fact]
    public async Task SectorRows_SumTheirNames_AndNamesWithoutASectorAreUnclassified()
    {
        _cards["AAA"] = Cards.Scored("AAA", sector: "Technology");
        _cards["BBB"] = Cards.Scored("BBB", sector: "Technology");
        Book(BookOf(8000m, Pos("AAA", 1000m), Pos("CCC", 1000m)));

        var result = await Gap();

        var tech = result.Sectors.Single(s => s.Sector == "Technology");
        tech.Symbols.Should().Equal("AAA", "BBB");
        tech.TargetPct.Should().Be(24m);
        tech.CurrentPct.Should().Be(10m);
        tech.GapPct.Should().Be(14m);
        tech.Status.Should().Be(GapStatus.Underweight);
        var none = result.Sectors.Single(s => s.Sector == GapModeCalculator.Unclassified);
        none.Symbols.Should().Equal("CCC");
        none.Status.Should().Be(GapStatus.Underweight);
    }

    [Fact]
    public async Task Amounts_AreRoundedConsistently_AndTotalsAddUp()
    {
        // A total that does not divide into whole cents of any weight.
        Book(BookOf(5371.17m, Pos("AAA", 1111.11m), Pos("BBB", 777.77m), Pos("CCC", 2043.9m), Pos("VOO", 333.33m)));

        var result = await Gap();

        foreach (var row in result.Names)
        {
            row.GapPct.Should().Be(row.TargetPct - row.CurrentPct, row.Symbol);
            row.GapUsd.Should().Be(row.TargetUsd - row.CurrentUsd, row.Symbol);
            row.CurrentUsd.Should().Be(decimal.Round(row.CurrentUsd, 2), row.Symbol);
        }

        foreach (var row in result.Sleeves)
        {
            row.GapPct.Should().Be(row.TargetPct - row.CurrentPct, row.AssetClass);
            row.GapUsd.Should().Be(row.TargetUsd - row.CurrentUsd, row.AssetClass);
        }

        // Every dollar of the book lands in exactly one sleeve, and the target sleeves cover the whole book.
        result.Sleeves.Sum(s => s.CurrentUsd).Should().Be(result.BookValueUsd);
        result.Sleeves.Sum(s => s.TargetPct).Should().Be(100m);
        result.Sleeves.Sum(s => s.CurrentPct).Should().BeApproximately(100m, 0.01m * result.Sleeves.Count);
        // The equity names and the equity sleeve are the same dollars; the sectors regroup the same names.
        result.Names.Sum(n => n.CurrentUsd).Should().Be(result.Sleeves.Single(s => s.AssetClass == "Equities").CurrentUsd);
        result.Sectors.Sum(s => s.CurrentUsd).Should().Be(result.Names.Sum(n => n.CurrentUsd));
        result.Sectors.Sum(s => s.TargetPct).Should().Be(result.Names.Sum(n => n.TargetPct));
    }

    [Fact]
    public async Task CurrentWeights_AgreeWithAllocationVsTarget_ForTheSameBook()
    {
        var book = BookOf(5371.17m, Pos("AAA", 1111.11m), Pos("BBB", 777.77m), Pos("CCC", 2043.9m), Pos("BTC", 333.33m, AssetClassNormalizer.Crypto));
        Book(book);

        var result = await Gap();

        // The same arithmetic GetAllocationDriftQueryHandler uses for a sleeve's actual weight.
        static decimal Drift(decimal value, decimal total) => Math.Round(value / total * 100m, 2);
        result.Sleeves.Single(s => s.AssetClass == "Equities").CurrentPct.Should().Be(Drift(1111.11m + 777.77m + 2043.9m, book.TotalValueUsd));
        result.Sleeves.Single(s => s.AssetClass == "Crypto").CurrentPct.Should().Be(Drift(333.33m, book.TotalValueUsd));
        result.Sleeves.Single(s => s.AssetClass == "Cash").CurrentPct.Should().Be(Drift(5371.17m, book.TotalValueUsd));
    }

    [Fact]
    public async Task EveryResponse_CarriesTheInformationalLabel()
    {
        var empty = await Gap();
        Book(BookOf(6400m, Pos("AAA", 1200m), Pos("BBB", 1200m), Pos("CCC", 1200m)));
        var matched = await Gap();
        _policy.Setup(p => p.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConstructionPolicy([], [new PolicySleeve("Equities", 80m), new PolicySleeve("Bonds", 40m)], null, null));
        var refused = await Gap();

        foreach (var result in new[] { empty, matched, refused })
        {
            result.Label.Should().Be(GapModeCalculator.InformationalLabel);
            result.Label.Should().Contain("not a recommendation to buy or sell").And.Contain("places or prepares a trade");
        }
    }

    [Fact]
    public async Task APolicyThatCannotBeBuilt_IsRefused_AndGivesNoGap()
    {
        _policy.Setup(p => p.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConstructionPolicy([], [new PolicySleeve("Equities", 80m), new PolicySleeve("Bonds", 40m)], null, null));
        Book(BookOf(6400m, Pos("AAA", 3600m)));

        var result = await Gap();

        result.Refusal.Should().Contain("over 100%");
        result.Summary.Headline.Should().Be(result.Refusal);
        result.Names.Should().BeEmpty();
        result.Sleeves.Should().BeEmpty();
        result.Sectors.Should().BeEmpty();
    }

    [Fact]
    public async Task TheTarget_IsTheCoverageSetAndTheOwnersHeldNames_NeverAHardCodedList()
    {
        _coverage.Setup(c => c.ListTickersAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(["nvda"]);

        var result = await Gap();

        result.Names.Select(n => n.Symbol).Should().Equal("NVDA");
        result.Names.Single().TargetPct.Should().Be(12m);
    }

    [Fact]
    public async Task AGapUnderTheTolerance_ReadsAsOnTarget_AndAStaleBookIsNamed()
    {
        var book = BookOf(6400m, Pos("AAA", 1196m), Pos("BBB", 1204m), Pos("CCC", 1200m)) with
        {
            IsStale = true,
            StaleSources = ["brokerage"],
        };
        Book(book);

        var result = await Gap();

        result.Names.Single(n => n.Symbol == "AAA").Status.Should().Be(GapStatus.OnTarget);
        result.Names.Single(n => n.Symbol == "BBB").Status.Should().Be(GapStatus.OnTarget);
        result.BookStale.Should().BeTrue();
        result.StaleSources.Should().Equal("brokerage");
        result.Notes.Should().Contain(n => n.Contains("stale for brokerage"));
    }

    [Fact]
    public async Task SameBookAndTarget_GiveTheSameGap()
    {
        Book(BookOf(5000m, Pos("AAA", 1500m), Pos("VOO", 700m), Pos("BBB", 300m)));

        var first = await Gap();
        var second = await Gap();

        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task NonEquityHoldings_ShowOnlyAsSleeves_AndAnUnplannedClassIsOverweight()
    {
        Book(BookOf(5400m, Pos("AAA", 1200m), Pos("BBB", 1200m), Pos("CCC", 1200m), Pos("BTC", 1000m, AssetClassNormalizer.Crypto)));

        var result = await Gap();

        result.Names.Select(n => n.Symbol).Should().BeEquivalentTo("AAA", "BBB", "CCC");
        var crypto = result.Sleeves.Single(s => s.AssetClass == "Crypto");
        crypto.Should().Match<SleeveGapDto>(s => s.TargetPct == 0m && s.CurrentPct == 10m && s.Status == GapStatus.Overweight && s.GapUsd == -1000m);
        result.Summary.Headline.Should().Contain("Crypto over by 10 points");
    }

    [Fact]
    public async Task UnknownArchetype_IsRejected()
    {
        var act = () => Gap("yolo");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
