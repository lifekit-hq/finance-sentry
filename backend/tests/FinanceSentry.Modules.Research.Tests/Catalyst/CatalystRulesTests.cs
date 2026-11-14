namespace FinanceSentry.Modules.Research.Tests.Catalyst;

using FinanceSentry.Modules.Research.Domain.Catalyst;
using FluentAssertions;
using Xunit;

public sealed class CatalystRulesTests
{
    private static readonly string[] Watched = ["1.01", "2.02", "5.02", "8.01"];

    [Theory]
    [InlineData("2.02,9.01", new[] { "2.02", "9.01" })]
    [InlineData(" 1.01 , 1.01;8.01", new[] { "1.01", "8.01" })]
    [InlineData("", new string[0])]
    public void ParseItems_SplitsTrimsAndDeduplicates(string raw, string[] expected)
        => CatalystRules.ParseItems([raw]).Should().Equal(expected);

    [Fact]
    public void ParseItems_ReadsAnArrayAndNull()
    {
        CatalystRules.ParseItems(["2.02", "9.01", null]).Should().Equal("2.02", "9.01");
        CatalystRules.ParseItems(null).Should().BeEmpty();
    }

    [Fact]
    public void WatchedItems_KeepsOnlyTheFourItems_InWatchedOrder()
    {
        CatalystRules.WatchedItems(["9.01", "2.02", "1.01", "7.01"], Watched).Should().Equal("1.01", "2.02");
        CatalystRules.WatchedItems(["9.01", "7.01", "2.03"], Watched).Should().BeEmpty();
    }

    [Fact]
    public void Tag_IsStablePerItem_AndRecognisedAsACatalystNomination()
    {
        CatalystRules.Tag("2.02").Should().Be("catalyst: 8-K item 2.02 (results of operations)");
        CatalystRules.Tag("2.02").Should().Be(CatalystRules.Tag("2.02"));
        CatalystRules.Tag("3.01").Should().Be("catalyst: 8-K item 3.01");
        CatalystRules.IsCatalystTag(CatalystRules.Tag("1.01")).Should().BeTrue();
        CatalystRules.IsCatalystTag("scan: top-decile RS").Should().BeFalse();
        CatalystRules.IsCatalystTag(null).Should().BeFalse();
    }

    [Fact]
    public void Rank_PutsTheMostWantedItemFirst_ThenTheNewestFiling_ThenTheTicker()
    {
        var older = new DateOnly(2026, 11, 12);
        var newer = new DateOnly(2026, 11, 13);
        var filings = new[]
        {
            (Ticker: "ZZZ", Items: (IReadOnlyList<string>)["8.01"], Filed: newer),
            (Ticker: "BBB", Items: ["2.02"], Filed: older),
            (Ticker: "AAA", Items: ["2.02"], Filed: older),
            (Ticker: "CCC", Items: ["2.02"], Filed: newer),
            (Ticker: "DDD", Items: ["5.02", "1.01"], Filed: older),
        };

        var ranked = CatalystRules.Rank(filings, Watched, f => f.Items, f => f.Filed, f => f.Ticker).Select(f => f.Ticker);

        ranked.Should().Equal("DDD", "CCC", "AAA", "BBB", "ZZZ");
    }

    [Fact]
    public void IsNewerThanRemoval_NeverRemoved_Passes()
        => CatalystRules.IsNewerThanRemoval(new DateOnly(2026, 11, 1), null).Should().BeTrue();

    [Fact]
    public void IsNewerThanRemoval_NeedsAFilingDatedAfterTheRemovalDay()
    {
        var removed = new DateTimeOffset(2026, 11, 14, 1, 10, 0, TimeSpan.Zero);

        CatalystRules.IsNewerThanRemoval(new DateOnly(2026, 11, 13), removed).Should().BeFalse();
        CatalystRules.IsNewerThanRemoval(new DateOnly(2026, 11, 14), removed).Should().BeFalse("a filing dated the removal day may predate it");
        CatalystRules.IsNewerThanRemoval(new DateOnly(2026, 11, 15), removed).Should().BeTrue();
    }
}
