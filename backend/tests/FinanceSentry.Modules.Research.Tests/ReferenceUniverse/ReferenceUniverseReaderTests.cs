namespace FinanceSentry.Modules.Research.Tests.ReferenceUniverse;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using FluentAssertions;
using Xunit;
using static FinanceSentry.Modules.Research.Tests.ReferenceUniverse.ReferenceUniverseTestKit;

public sealed class ReferenceUniverseReaderTests
{
    private static async Task<(FinanceSentry.Modules.Research.Infrastructure.Persistence.ResearchDbContext Db, ReferenceClock Clock)> SeededAsync()
    {
        var db = NewDb();
        var clock = new ReferenceClock(Start);
        var feeds = new IReferenceListFeed[]
        {
            new ScriptedFeed(ReferenceSources.Sp500) { Next = () => [ScriptedFeed.List("sp500", "AAPL", "MU", "BRK-B")] },
            new ScriptedFeed(ReferenceSources.FinanceDatabase)
            {
                Next = () =>
                [
                    ScriptedFeed.List("sector:Information Technology", "AAPL", "MU"),
                    ScriptedFeed.List("industry:Semiconductors", "MU"),
                ],
            },
            new ScriptedFeed(ReferenceSources.Themes) { Next = () => [ScriptedFeed.List("theme:memory", "MU")] },
        };
        await Refresher(db, clock, null, feeds).RefreshAsync();
        return (db, clock);
    }

    [Fact]
    public async Task GetListsAsync_CataloguesEveryListWithKindSourceAndSize()
    {
        var (db, clock) = await SeededAsync();
        using var _ = db;

        var lists = await Reader(db, clock).GetListsAsync();

        lists.Should().BeEquivalentTo(
        [
            new ReferenceListInfo("industry:Semiconductors", "industry", "financedatabase", 1),
            new ReferenceListInfo("sector:Information Technology", "sector", "financedatabase", 2),
            new ReferenceListInfo("sp500", "index", "sp500", 3),
            new ReferenceListInfo("theme:memory", "theme", "themes", 1),
        ]);
    }

    [Fact]
    public async Task GetMembersAsync_MatchesTheKeyCaseInsensitivelyAndReturnsNullForAnUnknownList()
    {
        var (db, clock) = await SeededAsync();
        using var _ = db;
        var reader = Reader(db, clock);

        var members = await reader.GetMembersAsync("SECTOR:information technology");

        members!.Select(m => m.Ticker).Should().Equal("AAPL", "MU");
        (await reader.GetMembersAsync("theme:nope")).Should().BeNull();
    }

    [Fact]
    public async Task GetListsForTickerAsync_ReturnsEveryListTheTickerCarries_InAnySpelling()
    {
        var (db, clock) = await SeededAsync();
        using var _ = db;
        var reader = Reader(db, clock);

        (await reader.GetListsForTickerAsync("mu")).Should().Equal(
            "industry:Semiconductors", "sector:Information Technology", "sp500", "theme:memory");
        (await reader.GetListsForTickerAsync("BRK.B")).Should().Equal("sp500");
        (await reader.GetListsForTickerAsync("ZZZZ")).Should().BeEmpty();
        (await reader.GetListsForTickerAsync("  ")).Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatusAsync_ReportsAFreshDatasetAsNotStaleAndANeverRefreshedOneAsStale()
    {
        var (db, clock) = await SeededAsync();
        using var _ = db;

        var status = await Reader(db, clock).GetStatusAsync();

        var sp500 = status.Single(s => s.Source == "sp500");
        sp500.Stale.Should().BeFalse();
        sp500.LastSuccessAt.Should().Be(Start);
        sp500.LastMemberCount.Should().Be(3);
        sp500.ConsecutiveFailures.Should().Be(0);
        var never = status.Single(s => s.Source == "nasdaq100");
        never.Stale.Should().BeTrue();
        never.LastSuccessAt.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_AFailingRefreshCountsFailuresCarriesTheReasonAndGoesStaleAfterTheWindow()
    {
        var (db, clock) = await SeededAsync();
        using var _ = db;
        var broken = new ScriptedFeed(ReferenceSources.Sp500) { Next = () => throw new ReferenceFeedException("upstream is down") };
        var refresher = Refresher(db, clock, null, broken);
        for (var week = 1; week <= 2; week++)
        {
            clock.Advance(TimeSpan.FromDays(7));
            await refresher.Invoking(r => r.RefreshAsync()).Should().ThrowAsync<ReferenceUniverseRefreshException>();
        }

        var sp500 = (await Reader(db, clock).GetStatusAsync()).Single(s => s.Source == "sp500");

        sp500.ConsecutiveFailures.Should().Be(2);
        sp500.LastError.Should().Contain("upstream is down");
        sp500.LastSuccessAt.Should().Be(Start);
        sp500.Stale.Should().BeTrue("the last success is 14 days old, past the 10 day window");
    }

    [Fact]
    public async Task Query_ServesCatalogueMembersOrTickerListsAndFlagsStaleness()
    {
        var (db, clock) = await SeededAsync();
        using var _ = db;
        var handler = new GetReferenceUniverseQueryHandler(Reader(db, clock));

        var catalogue = await handler.Handle(new GetReferenceUniverseQuery(null, null, null), default);
        var members = await handler.Handle(new GetReferenceUniverseQuery("sp500", null, 2), default);
        var forTicker = await handler.Handle(new GetReferenceUniverseQuery("sp500", "mu", null), default);
        var unknown = await handler.Handle(new GetReferenceUniverseQuery("theme:nope", null, null), default);

        catalogue.Lists.Should().HaveCount(4);
        catalogue.Note.Should().Contain("stale", "nasdaq100 was never refreshed in this fixture");
        members.MemberCount.Should().Be(3);
        members.Members.Should().HaveCount(2, "the limit caps the listing, not the count");
        forTicker.ListsForTicker.Should().Contain("theme:memory");
        forTicker.Members.Should().BeNull("a ticker query takes precedence over the list");
        unknown.Note.Should().Contain("No list 'theme:nope'");
    }
}
