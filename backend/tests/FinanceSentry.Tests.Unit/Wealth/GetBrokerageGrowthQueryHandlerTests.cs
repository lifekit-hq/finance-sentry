namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Domain.Ports;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>S1: the brokerage sleeve's contributions against its growth (M2) and its money-weighted return (M4).</summary>
public class GetBrokerageGrowthQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly D0 = new(2026, 7, 1);

    private readonly Mock<IBrokerageValueHistoryReader> _history = new();
    private readonly Mock<IBrokerageFlowReader> _flows = new();

    private static DateOnly Day(int n) => D0.AddDays(n);

    private static DailyBrokerageValue Value(int day, decimal total) => new(Day(day), total, total);

    private void Setup(DailyBrokerageValue[] values, FlowSourceCoverage[] sources, params SourceFlow[] flows)
    {
        _history.Setup(h => h.GetDailyAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(values);
        _flows.Setup(f => f.ReadAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrokerageFlowReading(sources, flows));
    }

    private Task<BrokerageGrowthResponse> Handle()
        => new GetBrokerageGrowthQueryHandler(_history.Object, _flows.Object)
            .Handle(new GetBrokerageGrowthQuery(UserId), CancellationToken.None);

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
        result.SourcesWithoutFlows.Should().Equal("ibkr");
        result.Headline.Amount.Should().BeNull();
        result.Headline.Quality.Should().Be(HeadlineQuality.Unavailable);
        result.Headline.Reasons.Should().Contain(new HeadlineReason(HeadlineReasonCode.UnknownFlow, "ibkr"));
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
}
