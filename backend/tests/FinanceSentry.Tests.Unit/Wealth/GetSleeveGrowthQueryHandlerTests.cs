namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Ports;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>S1/S2: a sleeve's contributions against its growth (M2) and its money-weighted return (M4).</summary>
public class GetSleeveGrowthQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly D0 = new(2026, 7, 1);

    private readonly Mock<IBrokerageValueHistoryReader> _history = new();
    private readonly Mock<IBrokerageFlowReader> _flows = new();
    private readonly Mock<INetWorthSnapshotRepository> _snapshots = new();
    private readonly Mock<ICryptoFlowReader> _cryptoFlows = new();

    private static DateOnly Day(int n) => D0.AddDays(n);

    private static DailyBrokerageValue Value(int day, decimal total) => new(Day(day), total, total);

    private void Setup(DailyBrokerageValue[] values, FlowSourceCoverage[] sources, params SourceFlow[] flows)
    {
        _history.Setup(h => h.GetDailyAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(values);
        _flows.Setup(f => f.ReadAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SleeveFlowReading(sources, flows));
    }

    private Task<SleeveGrowthResponse> Handle(string sleeve = WealthSourceIds.Brokerage)
        => new GetSleeveGrowthQueryHandler(_history.Object, _flows.Object, _snapshots.Object, _cryptoFlows.Object)
            .Handle(new GetSleeveGrowthQuery(UserId, sleeve), CancellationToken.None);

    private void SetupCrypto(NetWorthSnapshot[] snapshots, FlowSourceCoverage[] sources, params SourceFlow[] flows)
        => SetupCrypto(snapshots, new SleeveFlowReading(sources, flows));

    private void SetupCrypto(NetWorthSnapshot[] snapshots, SleeveFlowReading reading)
    {
        _snapshots.Setup(r => r.GetByUserIdUnscopedAsync(UserId, It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);
        _snapshots.Setup(r => r.GetSourcesUnscopedAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _cryptoFlows.Setup(f => f.ReadAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reading);
    }

    private static NetWorthSnapshot CryptoClose(int day, decimal crypto, string? staleSleeves = null)
        => new() { Id = Guid.NewGuid(), UserId = UserId, SnapshotDate = Day(day), CryptoTotal = crypto, BrokerageTotal = 99_999m, StaleSleeves = staleSleeves };

    private static SourceFlow RevolutXFlow(int day, FlowClass flowClass, decimal usd)
        => new("revolut_x", new ClassifiedFlow(Day(day), flowClass, usd), FxStatus.Exact);

    private static SourceFlow Flow(int day, FlowClass flowClass, decimal usd)
        => new("ibkr", new ClassifiedFlow(Day(day), flowClass, usd), FxStatus.Exact);

    private static readonly FlowSourceCoverage IbkrRecorded = new("ibkr", Day(-30), true);

    [Fact]
    public async Task SplitsTheChangeIntoContributionsAndGrowth_WithModifiedDietz()
    {
        Setup(
            [Value(0, 1000m), Value(5, 1150m), Value(10, 1210m)],
            [IbkrRecorded],
            Flow(1, FlowClass.ExternalInflow, 100m), Flow(6, FlowClass.Income, 15m), Flow(7, FlowClass.Tax, 3m));

        var result = await Handle();

        result.SeriesStart.Should().Be(Day(0));
        result.From.Should().Be(Day(0));
        result.To.Should().Be(Day(10));
        result.StartValue.Should().Be(1000m);
        result.EndValue.Should().Be(1210m);
        result.Contributed.Should().Be(100m);
        result.NewlyLinked.Should().Be(0m);
        result.Growth.Should().Be(110m);
        result.Income.Should().Be(15m);
        result.Taxes.Should().Be(3m);
        // The day-one deposit is invested for the whole window: 110 / (1000 + 100).
        result.MoneyWeightedReturn.Should().Be(0.1m);
        result.TimeWeightedReturn.Should().Be(0.1m, "the deposit landed before any market move, so timing cost nothing");
        result.SourcesWithoutFlows.Should().BeEmpty();
        result.Headline.Amount.Should().Be(110m);
        result.Headline.Percent.Should().Be(10m);
        result.Headline.Method.Should().Be(HeadlineMethod.ModifiedDietz);
        result.Headline.Quality.Should().Be(HeadlineQuality.Complete);
        result.Headline.Scope.Should().Be(HeadlineScope.Sleeve(WealthSourceIds.Brokerage));
    }

    [Fact]
    public async Task WithholdsTheSplit_WhileAProviderHasNeverReportedADeposit()
    {
        Setup([Value(0, 1000m), Value(10, 1210m)], [new FlowSourceCoverage("ibkr", Day(-30), false)]);

        var result = await Handle();

        result.StartValue.Should().Be(1000m);
        result.EndValue.Should().Be(1210m);
        result.Contributed.Should().BeNull();
        result.Growth.Should().BeNull();
        result.MoneyWeightedReturn.Should().BeNull();
        result.TimeWeightedReturn.Should().BeNull();
        result.SourcesWithoutFlows.Should().Equal("ibkr");
        result.Headline.Amount.Should().BeNull();
        result.Headline.Quality.Should().Be(HeadlineQuality.Unavailable);
        result.Headline.Reasons.Should().Contain(new HeadlineReason(HeadlineReasonCode.UnknownFlow, "ibkr"));
    }

    [Fact]
    public async Task SeparatesTheInvestmentsReturn_FromTheMoneysReturn_ByTheTimingOfTheDeposit()
    {
        // The market halves, then 1000 goes in on day 6 and the market doubles: the investments broke even,
        // the money - mostly invested for the rise - gained.
        Setup([Value(0, 1000m), Value(5, 500m), Value(10, 3000m)], [IbkrRecorded], Flow(6, FlowClass.ExternalInflow, 1000m));

        var result = await Handle();

        result.TimeWeightedReturn.Should().Be(0m);
        result.MoneyWeightedReturn.Should().BePositive();
    }

    [Fact]
    public async Task NamesTheValueANewlyLinkedProviderBrought_AsNeitherContributionNorGrowth()
    {
        Setup(
            [Value(0, 1000m), Value(1, 1010m), Value(2, 1500m), Value(3, 1520m), Value(4, 1530m)],
            [IbkrRecorded, new FlowSourceCoverage("inzhur", Day(2), true)]);

        var result = await Handle();

        result.NewlyLinked.Should().Be(510m);
        result.Contributed.Should().Be(0m);
        result.Growth.Should().Be(20m);
        result.Headline.Reasons.Should().Contain(new HeadlineReason(HeadlineReasonCode.ScopeChanged));
    }

    [Fact]
    public async Task StartsTheSeriesAtTheFirstValuedClose()
    {
        Setup([Value(0, 0m), Value(1, 1000m), Value(2, 1010m)], [IbkrRecorded]);

        var result = await Handle();

        result.SeriesStart.Should().Be(Day(1));
        result.From.Should().Be(Day(1));
        result.Growth.Should().Be(10m);
    }

    [Fact]
    public async Task IsUnavailableWithShortHistory_BeforeTwoValuedCloses()
    {
        Setup([Value(0, 1000m)], [IbkrRecorded]);

        var result = await Handle();

        result.SeriesStart.Should().Be(Day(0));
        result.From.Should().BeNull();
        result.Growth.Should().BeNull();
        result.Headline.Quality.Should().Be(HeadlineQuality.Unavailable);
        result.Headline.Reasons.Should().Contain(r => r.Code == HeadlineReasonCode.ShortHistory);
    }

    [Fact]
    public async Task CryptoSleeve_SplitsTheVenuesValueChange_FromTheVenuesFlows()
    {
        // Fixture 19 at EURUSD 1.10: EUR 500 in from the owner's Revolut app, then a EUR 100 card buy with a EUR 2 fee.
        SetupCrypto(
            [CryptoClose(0, 1000m), CryptoClose(10, 1700m)],
            [new FlowSourceCoverage("revolut_x", Day(-30), true)],
            RevolutXFlow(3, FlowClass.ExternalInflow, 550m),
            RevolutXFlow(4, FlowClass.ExternalInflow, 110m),
            RevolutXFlow(4, FlowClass.Fee, 2.20m));

        var result = await Handle(WealthSourceIds.Crypto);

        result.StartValue.Should().Be(1000m, "the crypto column, never the brokerage one");
        result.EndValue.Should().Be(1700m);
        result.Contributed.Should().Be(660m);
        result.Fees.Should().Be(2.20m);
        result.Growth.Should().Be(40m);
        result.Headline.Scope.Should().Be(HeadlineScope.Sleeve(WealthSourceIds.Crypto));
        _flows.Verify(f => f.ReadAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        _history.Verify(h => h.GetDailyAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CryptoSleeve_WithholdsTheSplit_WhileAVenuesFlowsAreUnknown()
    {
        SetupCrypto(
            [CryptoClose(0, 1000m), CryptoClose(10, 1700m)],
            [new FlowSourceCoverage("binance", Day(-30), false), new FlowSourceCoverage("revolut_x", Day(-30), true)],
            RevolutXFlow(3, FlowClass.ExternalInflow, 550m));

        var result = await Handle(WealthSourceIds.Crypto);

        result.SourcesWithoutFlows.Should().Equal("binance");
        result.Contributed.Should().BeNull();
        result.Growth.Should().BeNull();
        result.Headline.Reasons.Should().Contain(new HeadlineReason(HeadlineReasonCode.UnknownFlow, "binance"));
    }

    [Fact]
    public async Task CryptoSleeve_CarriesOnlyTheCryptoSourcesOwnStaleness()
    {
        SetupCrypto(
            [CryptoClose(0, 1000m, staleSleeves: "brokerage"), CryptoClose(10, 1100m, staleSleeves: "crypto")],
            [new FlowSourceCoverage("revolut_x", Day(-30), true)]);

        var result = await Handle(WealthSourceIds.Crypto);

        result.Growth.Should().Be(100m);
        result.Headline.Reasons.Where(r => r.Code == HeadlineReasonCode.StaleSource)
            .Should().Equal(new HeadlineReason(HeadlineReasonCode.StaleSource, WealthSourceIds.Crypto));
    }

    [Fact]
    public async Task CryptoSleeve_Fixture12_CountsTheValueInTransitBetweenVenues_SoTheMoveCostsOnlyItsFee()
    {
        // Revolut X held 20 SOL at 150; 10.05 SOL left it on day 1 (0.05 fee) and 10.00 reached Binance on day 2.
        var venues = new[] { new FlowSourceCoverage("binance", Day(-30), true), new FlowSourceCoverage("revolut_x", Day(-30), true) };
        SetupCrypto(
            [CryptoClose(0, 3000m), CryptoClose(1, 1492.50m)],
            new SleeveFlowReading(venues, [RevolutXFlow(1, FlowClass.Fee, 7.50m)])
            {
                InTransit = [new InTransitValue("revolut_x", Day(1), Day(2), 1500m, FxStatus.Exact)],
            });

        var result = await Handle(WealthSourceIds.Crypto);

        result.EndValue.Should().Be(2992.50m);
        result.Contributed.Should().Be(0m);
        result.Fees.Should().Be(7.50m);
        result.Growth.Should().Be(-7.50m);
        result.MoneyWeightedReturn.Should().Be(-0.0025m);
        result.Headline.Reasons.Should().NotContain(r => r.Code == HeadlineReasonCode.UnmatchedTransfer);
    }

    [Fact]
    public async Task CryptoSleeve_ATransferReadAsExternalForWantOfAMatch_MarksTheHeadlineUnmatched()
    {
        // Fixture 13: 500 USDT reached Binance and no venue's send matched it.
        var venues = new[] { new FlowSourceCoverage("binance", Day(-30), true), new FlowSourceCoverage("revolut_x", Day(-30), true) };
        SetupCrypto(
            [CryptoClose(0, 1000m), CryptoClose(10, 1500m)],
            venues,
            new SourceFlow("binance", new ClassifiedFlow(Day(4), FlowClass.ExternalInflow, 500m), FxStatus.Exact, UnmatchedTransfer: true));

        var result = await Handle(WealthSourceIds.Crypto);

        result.Contributed.Should().Be(500m, "provisionally external: never growth");
        result.Growth.Should().Be(0m);
        result.Headline.Reasons.Should().Contain(new HeadlineReason(HeadlineReasonCode.UnmatchedTransfer, "binance"));
    }
}
