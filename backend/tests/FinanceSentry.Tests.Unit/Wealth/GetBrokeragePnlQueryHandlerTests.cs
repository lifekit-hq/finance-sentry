namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>Issue 982: the header's P&amp;L on net deposited, and when it is withheld. Synthetic figures only.</summary>
public class GetBrokeragePnlQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 10, 1);

    private readonly Mock<IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse>> _growth = new();
    private readonly Mock<IBrokeragePnlReader> _pnl = new();

    private static SleeveGrowthResponse Growth(
        decimal? start = 1000m, decimal? contributed = 500m, decimal? linked = 0m, IReadOnlyList<string>? missing = null)
        => new(
            From, From, To, start, 2000m, contributed, linked, 0m, 0m, 0m, 0m, 0m, missing ?? [],
            HeadlineFactory.Growth(0m, MeasuredRate.Of(0m), HeadlineScope.Sleeve(WealthSourceIds.Brokerage), From, To, HeadlineSignals.None));

    private static RealizedPnlReading Realized(
        decimal usd = 150m, int computed = 2, bool hasFills = true, string[]? unavailable = null)
        => new(usd, computed, [.. (unavailable ?? []).Select(s => new UnavailableRealizedSymbol("ibkr", s))], hasFills, FxStatus.Exact);

    private static UnrealizedPnlReading Unrealized(
        decimal usd = 300m, int measured = 3, int total = 3, decimal measuredValue = 1000m, decimal totalValue = 1000m,
        string[]? unverified = null)
        => new(usd, measuredValue - usd, measuredValue, totalValue, measured, total, unverified ?? []);

    private async Task<BrokeragePnlResponse> Run(SleeveGrowthResponse growth, RealizedPnlReading realized, UnrealizedPnlReading unrealized)
    {
        _growth.Setup(g => g.Handle(It.IsAny<GetSleeveGrowthQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(growth);
        _pnl.Setup(p => p.ReadAsync(UserId, growth.From, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrokeragePnlReading(realized, unrealized));
        return await new GetBrokeragePnlQueryHandler(_growth.Object, _pnl.Object)
            .Handle(new GetBrokeragePnlQuery(UserId), default);
    }

    [Fact]
    public async Task CompleteFigures_ArePutOverTheNetDeposited()
    {
        var result = await Run(Growth(), Realized(), Unrealized());

        result.NetDepositedUsd.Should().Be(1500m); // opening 1000 + contributed 500
        result.Realized.Status.Should().Be(PnlStatus.Complete);
        result.Realized.PercentOfNetDeposited.Should().Be(10m);
        result.Unrealized.Status.Should().Be(PnlStatus.Complete);
        result.Unrealized.PercentOfNetDeposited.Should().Be(20m);
    }

    [Fact]
    public async Task ValueLinkedInsideTheWindow_CountsAsCapitalIn()
    {
        var result = await Run(Growth(linked: 200m), Realized(), Unrealized());

        result.NetDepositedUsd.Should().Be(1700m);
    }

    [Fact]
    public async Task UnknownFlows_WithholdTheDenominator_AndSoThePercents_ButNotTheAmounts()
    {
        var result = await Run(
            Growth(contributed: null, linked: null, missing: ["ibkr"]), Realized(), Unrealized());

        result.NetDepositedUsd.Should().BeNull();
        result.SourcesWithoutFlows.Should().Equal("ibkr");
        result.Realized.Usd.Should().Be(150m);
        result.Realized.PercentOfNetDeposited.Should().BeNull();
        result.Unrealized.PercentOfNetDeposited.Should().BeNull();
    }

    [Fact]
    public async Task PartialUnrealized_KeepsItsAmountWithCoverage_ButNoPercent()
    {
        var result = await Run(
            Growth(), Realized(), Unrealized(usd: 100m, measured: 1, total: 3, measuredValue: 400m, totalValue: 1000m, unverified: ["BBB", "CCC"]));

        result.Unrealized.Status.Should().Be(PnlStatus.Partial);
        result.Unrealized.Usd.Should().Be(100m);
        result.Unrealized.PercentOfNetDeposited.Should().BeNull();
        result.Unrealized.Coverage.Should().Be(0.4m);
        result.Unrealized.UnavailableSymbols.Should().Equal("BBB", "CCC");
    }

    [Fact]
    public async Task NoVerifiedBasis_IsUnavailable_NotZero()
    {
        var result = await Run(Growth(), Realized(), Unrealized(usd: 0m, measured: 0, total: 2, measuredValue: 0m, totalValue: 500m, unverified: ["AAA", "BBB"]));

        result.Unrealized.Status.Should().Be(PnlStatus.Unavailable);
        result.Unrealized.Usd.Should().BeNull();
    }

    [Fact]
    public async Task PartialRealized_NamesTheSymbolsLeftOut_AndDropsThePercent()
    {
        var result = await Run(Growth(), Realized(usd: 150m, computed: 1, unavailable: ["OLD"]), Unrealized());

        result.Realized.Status.Should().Be(PnlStatus.Partial);
        result.Realized.Usd.Should().Be(150m);
        result.Realized.PercentOfNetDeposited.Should().BeNull();
        result.Realized.Measured.Should().Be(1);
        result.Realized.Total.Should().Be(2);
        result.Realized.UnavailableSymbols.Should().Equal("OLD");
    }

    [Fact]
    public async Task NoFills_RealizedIsUnavailable_NotZero()
    {
        var result = await Run(Growth(), Realized(usd: 0m, computed: 0, hasFills: false), Unrealized());

        result.Realized.Status.Should().Be(PnlStatus.Unavailable);
        result.Realized.Usd.Should().BeNull();
    }

    [Fact]
    public async Task FillsButNoSellInTheWindow_IsARealZero()
    {
        var result = await Run(Growth(), Realized(usd: 0m, computed: 0), Unrealized());

        result.Realized.Status.Should().Be(PnlStatus.Complete);
        result.Realized.Usd.Should().Be(0m);
    }

    [Fact]
    public async Task EverySellUnexplained_IsUnavailable_NotZero()
    {
        var result = await Run(Growth(), Realized(usd: 0m, computed: 0, unavailable: ["OLD"]), Unrealized());

        result.Realized.Status.Should().Be(PnlStatus.Unavailable);
        result.Realized.Usd.Should().BeNull();
        result.Realized.UnavailableSymbols.Should().Equal("OLD");
    }
}
