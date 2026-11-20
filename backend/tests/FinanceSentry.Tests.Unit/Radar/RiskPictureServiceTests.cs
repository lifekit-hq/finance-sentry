using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Domain;
using FluentAssertions;
using Moq;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using Xunit;

namespace FinanceSentry.Tests.Unit.Radar;

public sealed class RiskPictureServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IDailyBarRepository> _bars = new();
    private readonly Mock<IPortfolioValueSource> _portfolio = new();
    private readonly Mock<IBrokerageFlowReader> _flows = new();
    private readonly Mock<IRiskPictureInputsSource> _inputs = new();

    public RiskPictureServiceTests()
    {
        _bars.Setup(r => r.GetSinceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), default)).ReturnsAsync([]);
        _portfolio.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), default))
            .ReturnsAsync([]);
        _flows.Setup(f => f.ReadAsync(UserId, It.IsAny<DateOnly>(), default)).ReturnsAsync(SleeveFlowReading.Empty);
    }

    private RiskPictureService Service() => new(_bars.Object, _portfolio.Object, _flows.Object, _inputs.Object);

    private void SetupInputs(decimal? flag, decimal? tolerance, params RiskPosition[] positions)
        => _inputs.Setup(i => i.ReadAsync(UserId, default)).ReturnsAsync(new RiskPictureInputs(positions, flag, tolerance));

    [Fact]
    public async Task GetAsync_ReadsTopWeightsCryptoShareAndTheStoredFlag()
    {
        SetupInputs(
            0.3m,
            null,
            new RiskPosition("DRAM", AssetClassNormalizer.Equities, 0.378m),
            new RiskPosition("BTC", AssetClassNormalizer.Crypto, 0.19m),
            new RiskPosition("VWCE", AssetClassNormalizer.Equities, 0.1m),
            new RiskPosition("ETH", AssetClassNormalizer.Crypto, 0.05m));

        var picture = await Service().GetAsync(UserId);

        picture.Top1Symbol.Should().Be("DRAM");
        picture.Top1Weight.Should().Be(0.378m);
        picture.Top3Symbols.Should().Equal("DRAM", "BTC", "VWCE");
        picture.Top3Weight.Should().Be(0.668m);
        picture.CryptoShare.Should().Be(0.24m);
        picture.PositionFlag.Should().Be(0.3m);
        picture.Top1OverFlag.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_WithNoStoredFlag_DoesNotInventOne()
    {
        SetupInputs(null, 0.25m, new RiskPosition("DRAM", AssetClassNormalizer.Equities, 0.9m));

        var picture = await Service().GetAsync(UserId);

        picture.PositionFlag.Should().BeNull();
        picture.Top1OverFlag.Should().BeNull();
        picture.DrawdownTolerance.Should().Be(0.25m);
    }

    [Fact]
    public async Task GetAsync_WithNoHistory_GivesTypedReasonsNeverZero()
    {
        SetupInputs(null, null);

        var picture = await Service().GetAsync(UserId);

        picture.Volatility.Value.Should().BeNull();
        picture.Volatility.Reason.Should().NotBeNullOrEmpty();
        picture.MaxDrawdown.Value.Should().BeNull();
        picture.Beta.Value.Should().BeNull();
        picture.Beta.Reason.Should().NotBeNullOrEmpty();
        picture.Top1Weight.Should().BeNull();
        picture.CryptoShare.Should().BeNull();
        picture.DrawdownTolerance.Should().BeNull();
    }

    private static DailyPortfolioValue Day(DateOnly date, decimal total) => new(date, total, null, total);

    private static DailyBar Bar(DateOnly date, decimal adjClose) => new()
    {
        Id = Guid.NewGuid(),
        Ticker = "SPY",
        Date = date,
        Open = adjClose,
        High = adjClose,
        Low = adjClose,
        Close = adjClose,
        AdjClose = adjClose,
        Volume = 1_000_000,
    };

    // Four month-end closes (all weekdays) give three monthly returns. SPY returns +1%, -2%, +3%;
    // the book returns twice each: +2%, -4%, +6%.
    private static readonly DateOnly[] MonthEnds =
        [new(2026, 1, 30), new(2026, 2, 27), new(2026, 3, 31), new(2026, 4, 30)];

    private void SetupBook() => _portfolio
        .Setup(p => p.GetAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), default))
        .ReturnsAsync([
            Day(MonthEnds[0], 100_000m), Day(MonthEnds[1], 102_000m), Day(MonthEnds[2], 97_920m), Day(MonthEnds[3], 103_795.2m)]);

    [Fact]
    public async Task GetAsync_BookMovingTwiceSpy_MeasuresBetaTwoAndTheBooksOwnVolatilityAndDrawdown()
    {
        SetupInputs(null, null);
        SetupBook();
        // Only the window opening one month before the book's first close returns bars: a wrong window finds none.
        _bars.Setup(r => r.GetSinceAsync("SPY", new DateOnly(2025, 12, 30), default)).ReturnsAsync(
            [Bar(MonthEnds[0], 400m), Bar(MonthEnds[1], 404m), Bar(MonthEnds[2], 395.92m), Bar(MonthEnds[3], 407.7976m)]);

        var picture = await Service().GetAsync(UserId);

        picture.MonthsObserved.Should().Be(3);
        picture.BetaMonths.Should().Be(3);
        picture.From.Should().Be(MonthEnds[0]);
        picture.To.Should().Be(MonthEnds[3]);
        picture.Beta.Value.Should().BeApproximately(2m, 0.0001m);
        picture.Beta.Reason.Should().BeNull();
        // Worked by hand: sample sd of {0.02, -0.04, 0.06} = 0.05033, times the square root of 12.
        picture.Volatility.Value.Should().BeApproximately(0.17436m, 0.0005m);
        // Index 1.00 -> 1.02 -> 0.9792 -> 1.0379: the fall from the 1.02 high is 4%.
        picture.MaxDrawdown.Value.Should().BeApproximately(0.04m, 0.0001m);
    }

    [Fact]
    public async Task GetAsync_WithNoSharedMonthsWithSpy_GivesBetaATypedReasonButKeepsTheBookFigures()
    {
        SetupInputs(null, null);
        SetupBook();
        _bars.Setup(r => r.GetSinceAsync("SPY", It.IsAny<DateOnly>(), default)).ReturnsAsync(
            [Bar(new DateOnly(2025, 1, 31), 400m), Bar(new DateOnly(2025, 2, 28), 404m), Bar(new DateOnly(2025, 3, 31), 396m)]);

        var picture = await Service().GetAsync(UserId);

        picture.BetaMonths.Should().Be(0);
        picture.Beta.Value.Should().BeNull();
        picture.Beta.Reason.Should().Be("NotEnoughHistory");
        picture.Volatility.Value.Should().NotBeNull();
        picture.MaxDrawdown.Value.Should().NotBeNull();
    }
}
