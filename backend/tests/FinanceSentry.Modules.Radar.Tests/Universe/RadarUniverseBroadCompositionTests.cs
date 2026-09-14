namespace FinanceSentry.Modules.Radar.Tests.Universe;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// Broad-universe composition (#558): with the flag on, the stage-1 shortlist widens the universe
/// past held + watchlist so momentum can be computed off the book; with it off, nothing changes and
/// stage 1 is never asked for a shortlist at all.
/// </summary>
public sealed class RadarUniverseBroadCompositionTests
{
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public async Task Sync_WithFlagOff_KeepsUniverseAtHeldAndWatched()
    {
        var (service, upserted, shortlist, _) = BuildService(broadEnabled: false, shortlisted: ["NVDA", "PLTR"]);

        await service.SyncAsync();

        upserted.Should().NotContain(m => m.Kind == UniverseKind.IndexConstituent);
        upserted.Select(m => m.Ticker).Should().Contain(["AAPL", "TSLA", "SPY"]);
        shortlist.Reads.Should().Be(0, "a disabled funnel must not pay stage 1's upstream cost");
    }

    [Fact]
    public async Task Sync_WithFlagOn_AddsShortlistedNamesAsIndexMembers()
    {
        var (service, upserted, _, _) = BuildService(broadEnabled: true, shortlisted: ["NVDA", "PLTR"]);

        await service.SyncAsync();

        upserted.Should().Contain(m => m.Ticker == "NVDA" && m.Kind == UniverseKind.IndexConstituent);
        upserted.Should().Contain(m => m.Ticker == "PLTR" && m.Kind == UniverseKind.IndexConstituent);
    }

    [Fact]
    public async Task Sync_WithFlagOn_KeepsOwnershipKindForATickerThatIsAlsoShortlisted()
    {
        var (service, upserted, _, _) = BuildService(broadEnabled: true, shortlisted: ["AAPL", "TSLA", "SPY"]);

        await service.SyncAsync();

        upserted.Single(m => m.Ticker == "AAPL").Kind.Should().Be(UniverseKind.Holding);
        upserted.Single(m => m.Ticker == "TSLA").Kind.Should().Be(UniverseKind.Watchlist);
        upserted.Single(m => m.Ticker == "SPY").Kind.Should().Be(UniverseKind.Benchmark);
    }

    [Fact]
    public async Task Sync_WithFlagOn_NormalisesAndDeduplicatesShortlistedNames()
    {
        var (service, upserted, _, _) = BuildService(broadEnabled: true, shortlisted: [" nvda ", "NVDA", ""]);

        await service.SyncAsync();

        upserted.Should().ContainSingle(m => m.Ticker == "NVDA");
        upserted.Should().NotContain(m => m.Ticker.Length == 0);
    }

    [Fact]
    public async Task Sync_DeactivatesANameYesterdaysShortlistCarriedAndTodaysDoesNot()
    {
        var (service, _, shortlist, deactivated) = BuildService(broadEnabled: true, shortlisted: ["NVDA"]);

        await service.SyncAsync();
        shortlist.Tickers = ["PLTR"];
        await service.SyncAsync();

        deactivated.Should().Equal(
            ["NVDA"], "the shortlist churns daily, and a member it drops must leave the universe with it");
    }

    private static Composition BuildService(bool broadEnabled, string[] shortlisted)
    {
        var upserted = new List<RadarUniverseMember>();
        var deactivated = new List<string>();

        var repo = new Mock<IRadarUniverseRepository>();
        repo.Setup(r => r.UpsertMembersAsync(It.IsAny<IReadOnlyCollection<RadarUniverseMember>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<RadarUniverseMember>, CancellationToken>((m, _) => upserted.AddRange(m))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => upserted);
        repo.Setup(r => r.DeactivateAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<string>, CancellationToken>((t, _) => deactivated.AddRange(t))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => upserted);

        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(User, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BrokerageHoldingSummary("AAPL", "STK", 10, 2000, DateTime.UtcNow, "ibkr")]);

        var watchlist = new Mock<IWatchlistReader>();
        watchlist.Setup(w => w.ListTickersAsync(User, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["TSLA"]);

        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([User]);

        var source = new FakeShortlistSource { Tickers = shortlisted };

        var options = TestSupport.Options(new RadarOptions { BroadUniverseEnabled = broadEnabled });
        var service = new RadarUniverseService(
            repo.Object, brokerage.Object, watchlist.Object, banking.Object, options, source);

        return new Composition(service, upserted, source, deactivated);
    }

    private sealed record Composition(
        RadarUniverseService Service,
        List<RadarUniverseMember> Upserted,
        FakeShortlistSource Shortlist,
        List<string> Deactivated);
}
