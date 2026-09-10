namespace FinanceSentry.Modules.Radar.Tests.Universe;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// Broad-universe composition (#558): with the flag on, index constituents widen the universe past
/// held + watchlist so momentum can be computed off the book; with it off, nothing changes.
/// </summary>
public sealed class RadarUniverseBroadCompositionTests
{
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public async Task Sync_WithFlagOff_KeepsUniverseAtHeldAndWatched()
    {
        var (service, upserted) = BuildService(broadEnabled: false, constituents: ["NVDA", "PLTR"]);

        await service.SyncAsync();

        upserted.Should().NotContain(m => m.Kind == UniverseKind.IndexConstituent);
        upserted.Select(m => m.Ticker).Should().Contain(["AAPL", "TSLA", "SPY"]);
    }

    [Fact]
    public async Task Sync_WithFlagOn_AddsConstituentsAsIndexMembers()
    {
        var (service, upserted) = BuildService(broadEnabled: true, constituents: ["NVDA", "PLTR"]);

        await service.SyncAsync();

        upserted.Should().Contain(m => m.Ticker == "NVDA" && m.Kind == UniverseKind.IndexConstituent);
        upserted.Should().Contain(m => m.Ticker == "PLTR" && m.Kind == UniverseKind.IndexConstituent);
    }

    [Fact]
    public async Task Sync_WithFlagOn_KeepsOwnershipKindForATickerThatIsAlsoAConstituent()
    {
        var (service, upserted) = BuildService(broadEnabled: true, constituents: ["AAPL", "TSLA", "SPY"]);

        await service.SyncAsync();

        upserted.Single(m => m.Ticker == "AAPL").Kind.Should().Be(UniverseKind.Holding);
        upserted.Single(m => m.Ticker == "TSLA").Kind.Should().Be(UniverseKind.Watchlist);
        upserted.Single(m => m.Ticker == "SPY").Kind.Should().Be(UniverseKind.Benchmark);
    }

    [Fact]
    public async Task Sync_WithFlagOn_NormalisesAndDeduplicatesConstituents()
    {
        var (service, upserted) = BuildService(broadEnabled: true, constituents: [" nvda ", "NVDA", ""]);

        await service.SyncAsync();

        upserted.Should().ContainSingle(m => m.Ticker == "NVDA");
        upserted.Should().NotContain(m => m.Ticker.Length == 0);
    }

    private static (RadarUniverseService Service, List<RadarUniverseMember> Upserted) BuildService(
        bool broadEnabled, string[] constituents)
    {
        var upserted = new List<RadarUniverseMember>();

        var repo = new Mock<IRadarUniverseRepository>();
        repo.Setup(r => r.UpsertMembersAsync(It.IsAny<IReadOnlyCollection<RadarUniverseMember>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<RadarUniverseMember>, CancellationToken>((m, _) => upserted.AddRange(m))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
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

        var source = new Mock<IIndexConstituentSource>();
        source.Setup(s => s.GetConstituents()).Returns(constituents);

        var options = TestSupport.Options(new RadarOptions { BroadUniverseEnabled = broadEnabled });
        var service = new RadarUniverseService(
            repo.Object, brokerage.Object, watchlist.Object, banking.Object, options, source.Object);

        return (service, upserted);
    }
}
