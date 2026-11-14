namespace FinanceSentry.Modules.Research.Tests.Coverage;

using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FluentAssertions;
using Xunit;

public sealed class CoverageUniverseReaderTests
{
    private static readonly DateTimeOffset Day1 = CoverageHarness.Day1;

    [Fact]
    public async Task ListsTheMembers_FreshestActivityFirst_ThenByTicker()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("OLD", addedAt: Day1.AddDays(-50));
        await h.MemberAsync("FRESH", addedAt: Day1.AddDays(-50), lastEvent: Day1.AddDays(-1));
        await h.MemberAsync("AAA", addedAt: Day1.AddDays(-5));
        await h.MemberAsync("BBB", addedAt: Day1.AddDays(-5));

        var tickers = await new CoverageUniverseReader(h.Coverage).ListTickersAsync(CoverageHarness.UserId);

        tickers.Should().Equal("FRESH", "AAA", "BBB", "OLD");
    }

    [Fact]
    public async Task AnotherUsersMembers_AreNotListed_AndAnEmptyUniverseIsEmpty()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("MU");

        (await new CoverageUniverseReader(h.Coverage).ListTickersAsync(Guid.NewGuid())).Should().BeEmpty();
    }

    [Fact]
    public async Task TheScreenReadsTheUniverseTheEvaluationBuilt()
    {
        var h = new CoverageHarness();
        h.Hold("NVDA");
        h.WatchedTickers.Add("AVGO");
        await h.RunAsync(Day1);

        var tickers = await new CoverageUniverseReader(h.Coverage).ListTickersAsync(CoverageHarness.UserId);

        tickers.Should().BeEquivalentTo("NVDA", "AVGO");
    }
}
