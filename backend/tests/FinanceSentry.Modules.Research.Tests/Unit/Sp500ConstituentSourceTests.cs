namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Xunit;

/// <summary>
/// The checked-in index seed is the breadth source for both the analyst universe and (via the Core
/// port) Radar's broad-universe bar ingestion — a malformed or empty resource silently narrows both.
/// </summary>
public sealed class Sp500ConstituentSourceTests
{
    [Fact]
    public void GetConstituents_ReadsTheEmbeddedSeed()
    {
        var constituents = new Sp500ConstituentSource().GetConstituents();

        constituents.Should().HaveCountGreaterThan(100);
        constituents.Should().Contain("AAPL");
    }

    [Fact]
    public void GetConstituents_IsNormalisedAndDeduplicated()
    {
        var constituents = new Sp500ConstituentSource().GetConstituents();

        constituents.Should().OnlyContain(t => t == t.ToUpperInvariant() && t.Trim() == t);
        constituents.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GetConstituents_CachesTheParsedList()
    {
        var source = new Sp500ConstituentSource();

        source.GetConstituents().Should().BeSameAs(source.GetConstituents());
    }
}
