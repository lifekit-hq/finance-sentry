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
        config.GetSection(PortfolioConstructionOptions.SectionName).Bind(options);

        options.Archetypes.Keys.Should().BeEquivalentTo("growth", "balanced", "cautious");
        options.Archetypes["cautious"].NameCount.Should().Be(20);
        options.Archetypes["growth"].MaxPositionPct.Should().Be(9m);
        options.Archetypes["growth"].MinStructure.Should().Be(60, "an override changes only the keys it names");
    }
}
