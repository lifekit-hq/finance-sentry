namespace FinanceSentry.Modules.Research.Tests.Scorecards;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FluentAssertions;
using Moq;
using Xunit;

public sealed class ScorecardSnapshotReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Snapshot_IsTheDossierScorecard_Flattened()
    {
        var handler = new Mock<IQueryHandler<GetScorecardQuery, ScorecardResult>>();
        handler.Setup(h => h.Handle(new GetScorecardQuery("AAA"), It.IsAny<CancellationToken>())).ReturnsAsync(Card());

        var snapshot = await new ScorecardSnapshotReader(handler.Object).GetAsync("AAA");

        snapshot.StructureRaw.Should().Be(64);
        snapshot.StructureAdjusted.Should().Be(68);
        snapshot.StructureBasis.Should().Be("adjusted");
        snapshot.Fundamentals.Should().Be(72);
        snapshot.Crowding.Should().Be("Normal");
        snapshot.ValuationPremiumPct.Should().Be(16.7m);
        snapshot.VerdictWord.Should().Be("Buy");
        snapshot.RulesVersion.Should().Be(1);
        snapshot.FiredRuleId.Should().Be("buy");
        snapshot.FiredRuleDescription.Should().Be("Structure and fundamentals clear the BUY bars.");
        snapshot.Sector.Should().Be("Technology");
        snapshot.Industry.Should().Be("Software - Infrastructure");
    }

    [Fact]
    public async Task Snapshot_OfAScorecardWithNoSectorOrIndustry_HasNullSectorAndIndustry()
    {
        var handler = new Mock<IQueryHandler<GetScorecardQuery, ScorecardResult>>();
        handler.Setup(h => h.Handle(new GetScorecardQuery("AAA"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Card() with { Sector = null, Industry = null });

        var snapshot = await new ScorecardSnapshotReader(handler.Object).GetAsync("AAA");

        snapshot.Sector.Should().BeNull();
        snapshot.Industry.Should().BeNull();
    }

    [Fact]
    public void Snapshot_CarriesNoCompositeNumber()
    {
        var names = typeof(FinanceSentry.Modules.Research.Domain.Ports.ScorecardSnapshot).GetProperties().Select(p => p.Name);

        names.Should().NotContain(n => n.Contains("Composite") || n.Contains("Overall") || n.Contains("Weighted") || n.Contains("Total"));
    }

    private static ScorecardResult Card() => new(
        "AAA", Now, 1,
        new ScorecardStructureAxis(64, 68, 4, ["calm"], "Calm", "Stable", Now, new Dictionary<int, decimal?>(), new Dictionary<int, decimal?>(), 0.04m, null, 1.1m, null, null, null, []),
        new ScorecardFundamentalsAxis(72, 0.1m, 0.4m, 0.01m, 0.12m, "annual", []),
        new ScorecardCrowdingAxis("Normal", 0.04m, 1.1m, 0.05m, 0.15m, 2m, null),
        ScorecardValuationAxis.From(28m, 24m, 5, 25m, 100m, 109m, "USD", false),
        new VerdictDto(
            "Buy", 1, "buy", "adjusted",
            [new VerdictRuleDto("buy", "Buy", "Structure and fundamentals clear the BUY bars.", true, true, [])],
            [], null),
        "Technology",
        "Software - Infrastructure");
}
