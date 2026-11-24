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

/// <summary>The cost of investing for a sleeve: commissions, fees, FX conversion and withholding, year to date and as a share of returns.</summary>
public class GetSleeveCostsQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 11, 24);
    private static readonly DateOnly YearOpening = new(2025, 12, 31);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }

    private readonly Mock<IBrokerageCostReader> _brokerage = new();
    private readonly Mock<ICryptoCostReader> _crypto = new();
    private readonly Mock<IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse>> _growth = new();

    private static CostItem Cost(CostKind kind, int month, decimal usd, FxStatus fx = FxStatus.Exact)
        => new(kind, new DateOnly(2026, month, 1), usd, fx);

    private void BrokerageReads(CostReading reading)
        => _brokerage.Setup(r => r.ReadAsync(UserId, YearOpening, It.IsAny<CancellationToken>())).ReturnsAsync(reading);

    private void CryptoReads(CostReading reading)
        => _crypto.Setup(r => r.ReadAsync(UserId, YearOpening, It.IsAny<CancellationToken>())).ReturnsAsync(reading);

    // The year's growth is after costs, from the opening close on the day before the year.
    private void GrowthIs(string sleeve, decimal? growth, DateOnly? from = null)
        => _growth.Setup(g => g.Handle(
                It.Is<GetSleeveGrowthQuery>(q => q.UserId == UserId && q.Sleeve == sleeve && q.From == YearOpening),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SleeveGrowthResponse(
                null, from ?? YearOpening, Today, 0m, 0m, 0m, 0m, growth, null, null, null, null, [],
                HeadlineFactory.Growth(null, MeasuredRate.Fails(MeasureFailure.InsufficientData),
                    HeadlineScope.Sleeve(sleeve), YearOpening, Today, HeadlineSignals.None)));

    private Task<SleeveCostsResponse> Handle(string sleeve)
        => new GetSleeveCostsQueryHandler(_brokerage.Object, _crypto.Object, _growth.Object, new FixedClock())
            .Handle(new GetSleeveCostsQuery(UserId, sleeve), CancellationToken.None);

    [Fact]
    public async Task Brokerage_SumsEachKindAndTheirTotal_AndTakesTheShareOfReturnsBeforeCosts()
    {
        BrokerageReads(new CostReading(
            [
                Cost(CostKind.Commission, 2, 30m),
                Cost(CostKind.Fee, 3, 10m),
                Cost(CostKind.FxConversion, 4, 5m),
                Cost(CostKind.Withholding, 5, 45m),
                Cost(CostKind.Withholding, 6, -5m),
            ], 0));
        GrowthIs(WealthSourceIds.Brokerage, 915m);

        var result = await Handle(WealthSourceIds.Brokerage);

        result.AsOf.Should().Be(Today);
        result.YearStart.Should().Be(new DateOnly(2026, 1, 1));
        result.Commissions.Should().Be(30m);
        result.Fees.Should().Be(10m);
        result.FxConversion.Should().Be(5m);
        result.Withholding.Should().Be(40m);
        result.Total.Should().Be(85m);
        result.ShareOfReturns.Should().Be(0.085m); // 85 / (915 + 85)
        _crypto.Verify(r => r.ReadAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CryptoOnly_ReadsOnlyTheCryptoFees_AndLeavesTheBrokerageOut()
    {
        CryptoReads(new CostReading([Cost(CostKind.Commission, 2, 12m), Cost(CostKind.Commission, 3, 8m)], 2));
        GrowthIs(WealthSourceIds.Crypto, 180m);

        var result = await Handle(WealthSourceIds.Crypto);

        result.Commissions.Should().Be(20m);
        result.Fees.Should().Be(0m);
        result.FxConversion.Should().Be(0m);
        result.Withholding.Should().Be(0m);
        result.Total.Should().Be(20m);
        result.ShareOfReturns.Should().Be(0.1m); // 20 / (180 + 20)
        result.UnvaluedRows.Should().Be(2);
        _brokerage.Verify(r => r.ReadAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BothSleeves_EachCarriesOnlyItsOwnCostsAndGrowth()
    {
        BrokerageReads(new CostReading([Cost(CostKind.Commission, 2, 50m)], 0));
        CryptoReads(new CostReading([Cost(CostKind.Commission, 2, 20m)], 0));
        GrowthIs(WealthSourceIds.Brokerage, 450m);
        GrowthIs(WealthSourceIds.Crypto, 80m);

        var brokerage = await Handle(WealthSourceIds.Brokerage);
        var crypto = await Handle(WealthSourceIds.Crypto);

        brokerage.Total.Should().Be(50m);
        brokerage.ShareOfReturns.Should().Be(0.1m);
        crypto.Total.Should().Be(20m);
        crypto.ShareOfReturns.Should().Be(0.2m);
    }

    [Fact]
    public async Task ABookWithNoCosts_IsAllZerosWithNoShare_AndNeverReadsTheGrowth()
    {
        BrokerageReads(CostReading.Empty);

        var result = await Handle(WealthSourceIds.Brokerage);

        result.Total.Should().Be(0m);
        result.Commissions.Should().Be(0m);
        result.ShareOfReturns.Should().BeNull();
        result.UnvaluedRows.Should().Be(0);
        result.ApproximateRows.Should().Be(0);
        result.FxReasons.Should().BeEmpty();
        _growth.Verify(g => g.Handle(It.IsAny<GetSleeveGrowthQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ShareOfReturns_IsNullWhenTheGrowthIsUnknownOrTheReturnsBeforeCostsAreNotPositive()
    {
        BrokerageReads(new CostReading([Cost(CostKind.Commission, 2, 50m)], 0));
        GrowthIs(WealthSourceIds.Brokerage, null);
        (await Handle(WealthSourceIds.Brokerage)).ShareOfReturns.Should().BeNull();

        GrowthIs(WealthSourceIds.Brokerage, -100m);
        var losing = await Handle(WealthSourceIds.Brokerage);
        losing.ShareOfReturns.Should().BeNull();
        losing.Total.Should().Be(50m);
    }

    [Fact]
    public async Task ShareOfReturns_CountsOnlyTheCostsAfterTheGrowthWindowOpens()
    {
        // The value series starts on 1 June: the 100 paid in February is outside the window the growth was measured over.
        BrokerageReads(new CostReading([Cost(CostKind.Commission, 2, 100m), Cost(CostKind.Commission, 7, 25m)], 0));
        GrowthIs(WealthSourceIds.Brokerage, 225m, from: new DateOnly(2026, 6, 1));

        var result = await Handle(WealthSourceIds.Brokerage);

        result.Total.Should().Be(125m);
        result.ShareOfReturns.Should().Be(0.1m); // 25 / (225 + 25)
    }

    [Fact]
    public async Task RowsConvertedWithoutAPublishedRate_StayInTheFigureAndAreCountedWithTheirReason()
    {
        BrokerageReads(new CostReading(
            [
                Cost(CostKind.Fee, 2, 10m),
                Cost(CostKind.Fee, 3, 4m, FxStatus.Unknown1To1),
                Cost(CostKind.Fee, 4, 6m, FxStatus.Unknown1To1),
            ], 0));
        GrowthIs(WealthSourceIds.Brokerage, null);

        var result = await Handle(WealthSourceIds.Brokerage);

        result.Fees.Should().Be(20m);
        result.ApproximateRows.Should().Be(2);
        result.FxReasons.Should().Equal("FX_UNKNOWN_1TO1");
    }
}
