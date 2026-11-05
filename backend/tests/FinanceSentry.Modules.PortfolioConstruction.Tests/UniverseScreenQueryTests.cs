namespace FinanceSentry.Modules.PortfolioConstruction.Tests;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.PortfolioConstruction.API.Responses;
using FinanceSentry.Modules.PortfolioConstruction.Application.Queries;
using FinanceSentry.Modules.PortfolioConstruction.Application.Services;
using FinanceSentry.Modules.PortfolioConstruction.Domain;
using FinanceSentry.Modules.PortfolioConstruction.Domain.Ports;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

public sealed class UniverseScreenQueryTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Mock<IUniverseScorecardSource> _scorecards = new();
    private readonly Mock<IConstructionPolicySource> _policy = new();
    private readonly Mock<IHeldEquitySource> _held = new();
    private readonly Mock<IWatchlistReader> _watchlist = new();
    private readonly List<IReadOnlyList<string>> _asked = [];
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public UniverseScreenQueryTests()
    {
        _policy.Setup(p => p.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConstructionPolicy.None);
        _held.Setup(h => h.ListSymbolsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _watchlist.Setup(w => w.ListTickersAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _scorecards
            .Setup(s => s.GetAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<string> symbols, CancellationToken _) =>
            {
                _asked.Add(symbols);
                return Task.FromResult<IReadOnlyList<UniverseScorecard>>([.. symbols.Select(s => Cards.Scored(s))]);
            });
    }

    private GetUniverseScreenQueryHandler Handler(PortfolioConstructionOptions? options = null) =>
        new(_scorecards.Object, _policy.Object, _held.Object, _watchlist.Object, _cache,
            Options.Create(options ?? new PortfolioConstructionOptions { CuratedUniverse = ["AAA", "BBB", "CCC"] }),
            new FakeTimeProvider(Cards.ScoredAt));

    [Fact]
    public async Task ColdStartUser_SeesTheWholeCuratedUniverse_WithFourAxesAVerdictAndNoBookDependence()
    {
        var result = await Handler().Handle(new GetUniverseScreenQuery(UserId, null), CancellationToken.None);

        result.Rows.Select(r => r.Symbol).Should().BeEquivalentTo("AAA", "BBB", "CCC");
        result.Rows.Should().OnlyContain(r =>
            r.Structure != null && r.Fundamentals != null && r.Crowding != null && r.Valuation != null
            && r.Verdict != null && r.Sources.Count == 1 && r.Sources[0] == "curated");
        result.Archetype.Should().Be("growth");
        result.Archetypes.Select(a => a.Key).Should().Equal("balanced", "growth");
        result.Construction.Constraints.Should().OnlyContain(c => c.Source == ConstraintSource.ArchetypeDefault);
    }

    [Fact]
    public async Task Rows_CarryTheVerdictRuleAndVersion_AndRawBesideAdjustedStructure()
    {
        var result = await Handler().Handle(new GetUniverseScreenQuery(UserId, "growth"), CancellationToken.None);

        var row = result.Rows[0];
        row.Verdict!.Should().Be(new RowVerdictDto("Buy", 1, "buy", "Adjusted structure at least 50 and fundamentals at least 50."));
        row.Structure!.Raw.Should().Be(70);
        row.Structure.Adjusted.Should().Be(70);
        row.Structure.Basis.Should().Be("adjusted");
        row.Valuation!.PremiumPct.Should().Be(16.7m);
    }

    [Fact]
    public async Task SuggestedWeights_FollowTheArchetype_AndRankedNamesComeFirst()
    {
        _scorecards
            .Setup(s => s.GetAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Cards.Scored("AAA", "Wait"), Cards.Scored("BBB", structure: 80), Cards.Scored("CCC", structure: 90)]);

        var result = await Handler().Handle(new GetUniverseScreenQuery(UserId, "growth"), CancellationToken.None);

        result.Rows.Select(r => r.Symbol).Should().Equal("CCC", "BBB", "AAA");
        result.Rows[0].ConstructionRank.Should().Be(1);
        result.Rows[0].SuggestedWeightPct.Should().Be(12m);
        result.Rows[2].SuggestedWeightPct.Should().BeNull();
        result.Rows[2].Reason.Should().Contain("WAIT");
        result.Construction.NamesHeld.Should().Be(2);
    }

    [Fact]
    public async Task HeldAndWatchlistNames_JoinFirst_WithoutDuplicates_AndTheCapKeepsThem()
    {
        _held.Setup(h => h.ListSymbolsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(["hld", "AAA"]);
        _watchlist.Setup(w => w.ListTickersAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(["WCH", "hld"]);
        var options = new PortfolioConstructionOptions { CuratedUniverse = ["AAA", "BBB", "CCC"], MaxUniverseSize = 4 };

        var result = await Handler(options).Handle(new GetUniverseScreenQuery(UserId, null), CancellationToken.None);

        _asked.Single().Should().Equal("HLD", "AAA", "WCH", "BBB");
        result.Truncated.Should().BeTrue();
        result.UniverseCap.Should().Be(4);
        result.Rows.Single(r => r.Symbol == "HLD").Sources.Should().Equal("held", "watchlist");
        result.Rows.Single(r => r.Symbol == "AAA").Sources.Should().Equal("held", "curated");
    }

    [Fact]
    public async Task OneUnreadableScorecard_ShowsItsFailure_AndTheRestStillBuild()
    {
        _scorecards
            .Setup(s => s.GetAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([UniverseScorecard.Failed("AAA", "the scorecard could not be read"), Cards.Scored("BBB"), Cards.Scored("CCC")]);

        var result = await Handler().Handle(new GetUniverseScreenQuery(UserId, null), CancellationToken.None);

        var failed = result.Rows.Single(r => r.Symbol == "AAA");
        failed.FailureReason.Should().Be("the scorecard could not be read");
        failed.Verdict.Should().BeNull();
        failed.SuggestedWeightPct.Should().BeNull();
        result.Rows.Count(r => r.SuggestedWeightPct > 0).Should().Be(2);
    }

    [Fact]
    public async Task UserLimits_AreReadFromThePolicySource_AndLabelled()
    {
        _policy.Setup(p => p.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConstructionPolicy(["BBB"], null, 5m, null));

        var result = await Handler().Handle(new GetUniverseScreenQuery(UserId, null), CancellationToken.None);

        result.Construction.Constraints.Single(c => c.Key == "maxPositionPct").Source.Should().Be(ConstraintSource.YourRiskRules);
        result.Rows.Single(r => r.Symbol == "BBB").Reason.Should().Be("Excluded by your IPS.");
        result.Rows.Where(r => r.SuggestedWeightPct is not null).Should().OnlyContain(r => r.SuggestedWeightPct <= 5m);
    }

    [Fact]
    public async Task AScoredCard_IsReusedAcrossReads_ButAFailedOneIsTriedAgain()
    {
        _scorecards
            .Setup(s => s.GetAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<string> symbols, CancellationToken _) =>
            {
                _asked.Add(symbols);
                return Task.FromResult<IReadOnlyList<UniverseScorecard>>([.. symbols.Select(s => s == "AAA" ? UniverseScorecard.Failed(s, "down") : Cards.Scored(s))]);
            });
        var handler = Handler();

        await handler.Handle(new GetUniverseScreenQuery(UserId, null), CancellationToken.None);
        var second = await handler.Handle(new GetUniverseScreenQuery(UserId, null), CancellationToken.None);

        _asked.Should().HaveCount(2);
        _asked[0].Should().Equal("AAA", "BBB", "CCC");
        _asked[1].Should().Equal("AAA");
        second.Rows.Select(r => r.Symbol).Should().BeEquivalentTo("AAA", "BBB", "CCC");
        second.Rows.Single(r => r.Symbol == "AAA").FailureReason.Should().Be("down");
    }

    [Fact]
    public async Task UnknownArchetype_IsRejected()
    {
        var act = () => Handler().Handle(new GetUniverseScreenQuery(UserId, "yolo"), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EmptyUniverse_ReturnsAnExplicitEmptyScreen()
    {
        var options = new PortfolioConstructionOptions { CuratedUniverse = [] };

        var result = await Handler(options).Handle(new GetUniverseScreenQuery(UserId, null), CancellationToken.None);

        result.Rows.Should().BeEmpty();
        result.Construction.Notes.Should().Contain(n => n.Contains("universe is empty"));
    }

    [Fact]
    public void ResponseShapes_CarryNoCompositeWeightedOrOverallScore()
    {
        // SC-003: the only numbers on a row are axis facts, the construction rank (an ordering position) and a weight.
        var shapes = new[]
        {
            typeof(UniverseScreenResult), typeof(UniverseRowDto), typeof(RowVerdictDto), typeof(RowStructureDto),
            typeof(RowFundamentalsDto), typeof(RowCrowdingDto), typeof(RowValuationDto), typeof(ConstructionDto),
            typeof(AppliedConstraintDto), typeof(SleeveDto), typeof(ArchetypeOptionDto),
        };
        var allowedScoreNames = new[] { "Score", "ScoredAt" };

        var names = shapes.SelectMany(t => t.GetProperties()).Select(p => p.Name).ToList();

        names.Should().NotContain(n => n.Contains("Composite", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Overall", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Weighted", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Grade", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Rating", StringComparison.OrdinalIgnoreCase)
            || (n.Contains("Score", StringComparison.OrdinalIgnoreCase) && !allowedScoreNames.Contains(n)));
        typeof(RowFundamentalsDto).GetProperty("Score").Should().NotBeNull("fundamentals is itself a 0-100 axis score, not a roll-up");
    }
}
