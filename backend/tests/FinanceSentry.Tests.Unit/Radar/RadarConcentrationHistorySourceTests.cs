using FinanceSentry.Core.Domain;
using FinanceSentry.Integration;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Wealth.Domain.Ports;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.Radar;

public sealed class RadarConcentrationHistorySourceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 1, 1);

    private readonly Mock<IBookHoldingsHistoryReader> _holdings = new();
    private readonly Mock<IBookCashHistoryReader> _cash = new();
    private readonly Mock<IIpsAllocationReader> _ips = new();

    public RadarConcentrationHistorySourceTests()
    {
        _holdings.Setup(h => h.ListSinceAsync(UserId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _cash.Setup(c => c.GetDailyAsync(UserId, From, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _ips.Setup(i => i.GetCurrentAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((IpsAllocationPolicy?)null);
    }

    private RadarConcentrationHistorySource Source() => new(_holdings.Object, _cash.Object, _ips.Object);

    [Fact]
    public async Task ReadAsync_TurnsIpsWholePercentIntoFractionsUnderTheCanonicalAssetClasses()
    {
        _ips.Setup(i => i.GetCurrentAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new IpsAllocationPolicy(
            [
                new IpsAllocationSleeve("Cryptocurrency", 10m, 5m, 15m),
                new IpsAllocationSleeve("money market", 5m, 0m, 10m),
            ],
            5m,
            25m));

        var inputs = await Source().ReadAsync(UserId, From);

        inputs.Targets.Select(t => (t.AssetClass, t.TargetFraction)).Should().Equal(
            (AssetClassNormalizer.Crypto, 0.10m),
            (AssetClassNormalizer.Cash, 0.05m));
    }

    [Fact]
    public async Task ReadAsync_WithNoIps_HasNoTargets()
    {
        var inputs = await Source().ReadAsync(UserId, From);

        inputs.Targets.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_MapsTheCryptoSleeveToCrypto_IgnoringCase_AndKeepsEveryOtherSleeveOutOfIt()
    {
        var at = new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero);
        _holdings.Setup(h => h.ListSinceAsync(UserId, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new BookHoldingsCapture(at,
                [
                    new BookHoldingPosition("BTC", "Crypto", 60m),
                    new BookHoldingPosition("ETH", RiskSleeve.Crypto, 30m),
                    new BookHoldingPosition("AAA", RiskSleeve.Brokerage, 10m),
                ]),
            ]);

        var inputs = await Source().ReadAsync(UserId, From);

        var capture = inputs.Captures.Should().ContainSingle().Subject;
        capture.CapturedAt.Should().Be(at);
        capture.Positions.Select(p => (p.Symbol, p.IsCrypto, p.UsdValue)).Should().Equal(
            ("BTC", true, 60m), ("ETH", true, 30m), ("AAA", false, 10m));
    }

    [Fact]
    public async Task ReadAsync_PassesTheMeasuredCashDaysThrough()
    {
        _cash.Setup(c => c.GetDailyAsync(UserId, From, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DailyBookCash(new DateOnly(2026, 2, 1), 30m, 200m)]);

        var inputs = await Source().ReadAsync(UserId, From);

        inputs.Cash.Should().ContainSingle().Which.Should().Be(new ConcentrationCash(new DateOnly(2026, 2, 1), 30m, 200m));
    }
}
