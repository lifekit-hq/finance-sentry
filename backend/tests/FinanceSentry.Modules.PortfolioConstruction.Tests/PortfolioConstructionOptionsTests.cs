namespace FinanceSentry.Modules.PortfolioConstruction.Tests;

using FinanceSentry.Modules.PortfolioConstruction.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class PortfolioConstructionOptionsTests
{
    [Fact]
    public void Defaults_ShipGrowthAndBalanced_AndNothingElse()
    {
        var options = new PortfolioConstructionOptions();

        options.Archetypes.Keys.Should().BeEquivalentTo("growth", "balanced");
        options.DefaultArchetype.Should().Be("growth");
        options.Archetypes["growth"].Sleeves.Values.Sum().Should().Be(100m);
        options.Archetypes["balanced"].Sleeves.Values.Sum().Should().Be(100m);
        options.CuratedUniverse.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(options.MaxUniverseSize);
    }

    [Fact]
    public void ANewArchetype_IsAConfigurationChange()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PortfolioConstruction:Archetypes:cautious:Label"] = "Cautious",
            ["PortfolioConstruction:Archetypes:cautious:NameCount"] = "20",
            ["PortfolioConstruction:Archetypes:cautious:MaxPositionPct"] = "4",
            ["PortfolioConstruction:Archetypes:growth:MaxPositionPct"] = "9",
        }).Build();

        var options = new PortfolioConstructionOptions();
        PortfolioConstructionOptions.Bind(config.GetSection(PortfolioConstructionOptions.SectionName), options);

        options.Archetypes.Keys.Should().BeEquivalentTo("growth", "balanced", "cautious");
        options.Archetypes["cautious"].NameCount.Should().Be(20);
        options.Archetypes["growth"].MaxPositionPct.Should().Be(9m);
        options.Archetypes["growth"].MinStructure.Should().Be(60, "an override changes only the keys it names");
        options.Archetypes["growth"].Ordering.Should().Equal("structure", "fundamentals");
        options.Archetypes["cautious"].Sleeves.Should().BeEquivalentTo(
            new Dictionary<string, decimal> { ["Equities"] = 90m, ["Cash"] = 10m },
            "a list the configuration does not name keeps its default");
    }

    [Fact]
    public void ConfiguredCollections_ReplaceTheShippedOnes_NotMergeIntoThem()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PortfolioConstruction:CuratedUniverse:0"] = "AAPL",
            ["PortfolioConstruction:CuratedUniverse:1"] = "MSFT",
            ["PortfolioConstruction:Archetypes:strict:Label"] = "Strict",
            ["PortfolioConstruction:Archetypes:strict:EligibleVerdicts:0"] = "StrongBuy",
            ["PortfolioConstruction:Archetypes:strict:Ordering:0"] = "fundamentals",
            ["PortfolioConstruction:Archetypes:strict:Sleeves:Equities"] = "50",
            ["PortfolioConstruction:Archetypes:strict:Sleeves:Bonds"] = "50",
            ["PortfolioConstruction:Archetypes:balanced:Sleeves:Equities"] = "70",
            ["PortfolioConstruction:Archetypes:balanced:Sleeves:Cash"] = "30",
        }).Build();

        var options = new PortfolioConstructionOptions();
        PortfolioConstructionOptions.Bind(config.GetSection(PortfolioConstructionOptions.SectionName), options);

        options.CuratedUniverse.Should().Equal("AAPL", "MSFT");
        var strict = options.Archetypes["strict"];
        strict.EligibleVerdicts.Should().Equal("StrongBuy");
        strict.Ordering.Should().Equal("fundamentals");
        strict.Sleeves.Should().BeEquivalentTo(new Dictionary<string, decimal> { ["Equities"] = 50m, ["Bonds"] = 50m });
        options.Archetypes["balanced"].Sleeves.Should().BeEquivalentTo(
            new Dictionary<string, decimal> { ["Equities"] = 70m, ["Cash"] = 30m },
            "an override of an existing archetype replaces its sleeves rather than adding to them");
    }
}
