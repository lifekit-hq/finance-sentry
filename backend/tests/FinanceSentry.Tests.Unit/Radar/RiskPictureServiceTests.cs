using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
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
}
