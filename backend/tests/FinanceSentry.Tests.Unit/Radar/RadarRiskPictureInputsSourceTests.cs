using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Integration;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.Radar;

public sealed class RadarRiskPictureInputsSourceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IBookFiguresService> _book = new();
    private readonly Mock<IRiskLimitsReader> _limits = new();
    private readonly Mock<IRiskProfileReader> _profile = new();

    private RadarRiskPictureInputsSource Source() => new(_book.Object, _limits.Object, _profile.Object);

    private static BookFigurePosition Position(string symbol, string assetClass, decimal usd, string provider = "ibkr")
        => new(symbol, assetClass, 1m, null, usd, provider);

    private static BookFigures Book(decimal total, params BookFigurePosition[] positions)
        => new(total - positions.Sum(p => p.UsdValue), 0m, 0m, positions.Sum(p => p.UsdValue), total, positions, false, []);

    private static IpsRiskProfile Ips(decimal? tolerance)
        => new(3, null, tolerance, null, false, 10, null);

    private void Setup(BookFigures book, decimal? flag = null, decimal? tolerance = null)
    {
        _book.Setup(b => b.ReadAsync(UserId, default)).ReturnsAsync(book);
        _limits.Setup(l => l.GetCurrentAsync(UserId, default)).ReturnsAsync(new RiskLimits(flag, null));
        _profile.Setup(p => p.GetCurrentAsync(UserId, default)).ReturnsAsync(Ips(tolerance));
    }

    [Fact]
    public async Task ReadAsync_WeighsEachSymbolOverTheWholeBookCashIncluded_AndMergesVenues()
    {
        // 1,000 whole book: 300 + 100 of DRAM on two venues, 100 BTC, 500 cash.
        Setup(Book(
            1_000m,
            Position("DRAM", AssetClassNormalizer.Equities, 300m),
            Position("dram", AssetClassNormalizer.Equities, 100m, "revolut"),
            Position("BTC", AssetClassNormalizer.Crypto, 100m, "binance")));

        var inputs = await Source().ReadAsync(UserId);

        inputs.Positions.Should().HaveCount(2);
        var dram = inputs.Positions.Single(p => p.Symbol == "DRAM");
        dram.Weight.Should().Be(0.4m, "one symbol on two venues is one position over the whole book, not over invested value");
        inputs.Positions.Single(p => p.Symbol == "BTC").Weight.Should().Be(0.1m);
    }

    [Fact]
    public async Task ReadAsync_WithAnEmptyBook_GivesZeroWeightsNotADivisionError()
    {
        Setup(Book(0m, Position("DRAM", AssetClassNormalizer.Equities, 0m)));

        var inputs = await Source().ReadAsync(UserId);

        inputs.Positions.Single().Weight.Should().Be(0m);
    }

    [Theory]
    [InlineData(20, 0.2)]
    [InlineData(100, 1.0)]
    [InlineData(0.5, 0.005)]
    public async Task ReadAsync_TurnsAWholePercentToleranceIntoTheFraction(double percent, double fraction)
    {
        Setup(Book(100m), tolerance: (decimal)percent);

        var inputs = await Source().ReadAsync(UserId);

        inputs.DrawdownTolerance.Should().Be((decimal)fraction);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public async Task ReadAsync_ReadsAToleranceOutsideZeroToHundredAsNotSet(double percent)
    {
        Setup(Book(100m), tolerance: (decimal)percent);

        (await Source().ReadAsync(UserId)).DrawdownTolerance.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_WithNoIpsAndNoRuleSet_LeavesToleranceAndFlagNull()
    {
        _book.Setup(b => b.ReadAsync(UserId, default)).ReturnsAsync(Book(100m));
        _limits.Setup(l => l.GetCurrentAsync(UserId, default)).ReturnsAsync((RiskLimits?)null);
        _profile.Setup(p => p.GetCurrentAsync(UserId, default)).ReturnsAsync((IpsRiskProfile?)null);

        var inputs = await Source().ReadAsync(UserId);

        inputs.DrawdownTolerance.Should().BeNull();
        inputs.PositionFlag.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_PassesTheStoredPositionFlagThroughUnchanged()
    {
        Setup(Book(100m), flag: 0.3m);

        (await Source().ReadAsync(UserId)).PositionFlag.Should().Be(0.3m, "the rule set already stores a fraction");
    }
}
