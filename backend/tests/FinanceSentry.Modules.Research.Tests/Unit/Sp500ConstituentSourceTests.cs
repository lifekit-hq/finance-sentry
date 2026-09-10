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

    /// <summary>
    /// The seed drives per-ticker Yahoo bar ingestion, and Yahoo spells share classes with a dash
    /// (<c>BRK-B</c>, not <c>BRK.B</c>). A dotted symbol does not fail loudly — the source returns an
    /// empty series, so the ticker never gains a bar while staying permanently least-fresh and
    /// burning a rotation budget slot every run.
    /// </summary>
    [Fact]
    public void GetConstituents_SpellsShareClassesInYahooForm()
    {
        var constituents = new Sp500ConstituentSource().GetConstituents();

        constituents.Should().Contain("BRK-B");
        constituents.Should().OnlyContain(t => !t.Contains('.', StringComparison.Ordinal));
    }

    [Fact]
    public void GetConstituents_CachesTheParsedList()
    {
        var source = new Sp500ConstituentSource();

        source.GetConstituents().Should().BeSameAs(source.GetConstituents());
    }
}
