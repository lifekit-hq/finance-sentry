namespace FinanceSentry.Modules.Research.Tests.ReferenceUniverse;

using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;
using static FinanceSentry.Modules.Research.Tests.ReferenceUniverse.ReferenceUniverseTestKit;

public sealed class ReferenceUniverseRefresherTests
{
    private static string[] Tickers(ResearchDbContext db, string listKey) =>
        db.ReferenceListMembers.Where(m => m.ListKey == listKey).Select(m => m.Ticker).OrderBy(t => t).ToArray();

    [Fact]
    public async Task Refresh_StoresEveryListWithItsMembersAndLogsASuccessfulRun()
    {
        using var db = NewDb();
        var sp500 = new ScriptedFeed(ReferenceSources.Sp500) { Next = () => [ScriptedFeed.List("sp500", "AAPL", "MSFT")] };
        var themes = new ScriptedFeed(ReferenceSources.Themes)
        {
            Next = () => [ScriptedFeed.List("theme:memory", "MU", "AAPL")],
        };

        var outcomes = await Refresher(db, new ReferenceClock(Start), null, sp500, themes).RefreshAsync();

        outcomes.Should().OnlyContain(o => o.Succeeded);
        Tickers(db, "sp500").Should().Equal("AAPL", "MSFT");
        Tickers(db, "theme:memory").Should().Equal("AAPL", "MU");
        db.ReferenceListMembers.Count(m => m.Ticker == "AAPL").Should().Be(2, "a ticker carries every list that names it");
        db.ReferenceRefreshRuns.Should().OnlyContain(r => r.Succeeded && r.Error == null);
        db.ReferenceRefreshRuns.Single(r => r.Source == ReferenceSources.Sp500).MemberCount.Should().Be(2);
    }

    [Fact]
    public async Task Refresh_ReplacesTheDatasetSoDepartedMembersLeaveAndNewOnesJoin()
    {
        using var db = NewDb();
        var feed = new ScriptedFeed(ReferenceSources.Sp500) { Next = () => [ScriptedFeed.List("sp500", "AAPL", "TTD", "WBD")] };
        var refresher = Refresher(db, new ReferenceClock(Start), new ReferenceUniverseOptions { MinRetainedFraction = 0.5 }, feed);
        await refresher.RefreshAsync();

        feed.Next = () => [ScriptedFeed.List("sp500", "AAPL", "TWLO")];
        await refresher.RefreshAsync();

        Tickers(db, "sp500").Should().Equal("AAPL", "TWLO");
    }

    [Fact]
    public async Task Refresh_OfOneDatasetLeavesTheOthersUntouched()
    {
        using var db = NewDb();
        var sp500 = new ScriptedFeed(ReferenceSources.Sp500) { Next = () => [ScriptedFeed.List("sp500", "AAPL")] };
        var ndx = new ScriptedFeed(ReferenceSources.Nasdaq100) { Next = () => [ScriptedFeed.List("nasdaq100", "NVDA")] };
        await Refresher(db, new ReferenceClock(Start), null, sp500, ndx).RefreshAsync();

        sp500.Next = () => [ScriptedFeed.List("sp500", "MSFT")];
        await Refresher(db, new ReferenceClock(Start), null, sp500).RefreshAsync();

        Tickers(db, "sp500").Should().Equal("MSFT");
        Tickers(db, "nasdaq100").Should().Equal("NVDA");
    }

    [Fact]
    public async Task Refresh_Failure_IsLoudAndKeepsThePreviousMembers()
    {
        using var db = NewDb();
        var feed = new ScriptedFeed(ReferenceSources.Sp500) { Next = () => [ScriptedFeed.List("sp500", "AAPL", "MSFT")] };
        var clock = new ReferenceClock(Start);
        var refresher = Refresher(db, clock, null, feed);
        await refresher.RefreshAsync();

        clock.Advance(TimeSpan.FromDays(7));
        feed.Next = () => throw new ReferenceFeedException("download of https://up/sp500.csv failed: 503");
        var act = () => refresher.RefreshAsync();

        var thrown = (await act.Should().ThrowAsync<ReferenceUniverseRefreshException>()).Which;
        thrown.Failures.Should().ContainSingle().Which.Source.Should().Be(ReferenceSources.Sp500);
        thrown.Message.Should().Contain("503");
        Tickers(db, "sp500").Should().Equal("AAPL", "MSFT");
        var failed = db.ReferenceRefreshRuns.Single(r => !r.Succeeded);
        failed.Source.Should().Be(ReferenceSources.Sp500);
        failed.Error.Should().Contain("ReferenceFeedException").And.Contain("503");
        failed.FinishedAt.Should().Be(Start.AddDays(7));
    }

    [Fact]
    public async Task Refresh_OneFailingDatasetDoesNotStopTheOthersAndStillFailsTheRun()
    {
        using var db = NewDb();
        var broken = new ScriptedFeed(ReferenceSources.Nasdaq100) { Next = () => throw new InvalidOperationException("boom") };
        var good = new ScriptedFeed(ReferenceSources.Sp500) { Next = () => [ScriptedFeed.List("sp500", "AAPL")] };

        var act = () => Refresher(db, new ReferenceClock(Start), null, broken, good).RefreshAsync();

        await act.Should().ThrowAsync<ReferenceUniverseRefreshException>();
        Tickers(db, "sp500").Should().Equal("AAPL");
        db.ReferenceRefreshRuns.Count(r => r.Succeeded).Should().Be(1);
        db.ReferenceRefreshRuns.Single(r => !r.Succeeded).Error.Should().Contain("boom");
    }

    [Fact]
    public async Task Refresh_RefusesToApplyAPayloadThatWouldGutAList()
    {
        using var db = NewDb();
        var feed = new ScriptedFeed(ReferenceSources.Sp500)
        {
            Next = () => [ScriptedFeed.List("sp500", Enumerable.Range(0, 100).Select(i => $"T{i}").ToArray())],
        };
        var refresher = Refresher(db, new ReferenceClock(Start), null, feed);
        await refresher.RefreshAsync();

        feed.Next = () => [ScriptedFeed.List("sp500", "T1", "T2", "T3")];
        var act = () => refresher.RefreshAsync();

        (await act.Should().ThrowAsync<ReferenceUniverseRefreshException>()).WithMessage("*shrink*100*3*");
        Tickers(db, "sp500").Should().HaveCount(100);
        db.ReferenceRefreshRuns.Single(r => !r.Succeeded).Error.Should().Contain("refusing");
    }

    [Fact]
    public async Task Refresh_LetsAHandEditedThemeListShrinkFreely()
    {
        using var db = NewDb();
        var feed = new ScriptedFeed(ReferenceSources.Themes)
        {
            Next = () => [ScriptedFeed.List("theme:power", "VST", "CEG", "NRG", "TLN", "GEV")],
        };
        var refresher = Refresher(db, new ReferenceClock(Start), null, feed);
        await refresher.RefreshAsync();

        feed.Next = () => [ScriptedFeed.List("theme:power", "VST")];
        await refresher.RefreshAsync();

        Tickers(db, "theme:power").Should().Equal("VST");
    }
}
