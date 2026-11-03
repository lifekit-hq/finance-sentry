using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Modules.Risk.Tests;

public sealed class HoldingSnapshotBrokeragePriceHistoryReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static HoldingSnapshot Snap(DateTimeOffset at, string symbol, decimal quantity, decimal usd, string sleeve = RiskSleeve.Brokerage)
        => new() { UserId = UserId, Symbol = symbol, Sleeve = sleeve, Quantity = quantity, UsdValue = usd, CapturedAt = at };

    private static HoldingSnapshotBrokeragePriceHistoryReader ReaderOver(params HoldingSnapshot[] rows)
    {
        var repo = new Mock<IHoldingSnapshotRepository>();
        repo.Setup(r => r.ListSinceUnscopedAsync(UserId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        return new HoldingSnapshotBrokeragePriceHistoryReader(repo.Object);
    }

    [Fact]
    public async Task GetDailyPricesAsync_PricesEachSymbolPerShareFromTheLatestCaptureOfTheUtcDay()
    {
        var early = new DateTimeOffset(2027, 3, 1, 1, 0, 0, TimeSpan.Zero);
        var late = new DateTimeOffset(2027, 3, 1, 22, 0, 0, TimeSpan.Zero);
        var reader = ReaderOver(
            Snap(early, "AAA", 10m, 100m),
            Snap(late, "AAA", 10m, 120m),
            Snap(late, "BBB", 4m, 50m));

        var prices = await reader.GetDailyPricesAsync(UserId, new DateOnly(2027, 1, 1));

        prices.Should().Equal(
            new DailySymbolPrice("AAA", new DateOnly(2027, 3, 1), 12m),
            new DailySymbolPrice("BBB", new DateOnly(2027, 3, 1), 12.5m));
    }

    [Fact]
    public async Task GetDailyPricesAsync_LeavesOutOtherSleevesAndPositionsWithNoQuantity_AndOrdersByDate()
    {
        var reader = ReaderOver(
            Snap(new DateTimeOffset(2027, 3, 3, 2, 0, 0, TimeSpan.Zero), "AAA", 1m, 30m),
            Snap(new DateTimeOffset(2027, 3, 2, 2, 0, 0, TimeSpan.Zero), "AAA", 1m, 20m),
            Snap(new DateTimeOffset(2027, 3, 2, 2, 0, 0, TimeSpan.Zero), "COIN", 1m, 99m, RiskSleeve.Crypto),
            Snap(new DateTimeOffset(2027, 3, 2, 2, 0, 0, TimeSpan.Zero), "ZERO", 0m, 0m));

        var prices = await reader.GetDailyPricesAsync(UserId, new DateOnly(2027, 1, 1));

        prices.Select(p => (p.Symbol, p.Date)).Should().Equal(
            ("AAA", new DateOnly(2027, 3, 2)), ("AAA", new DateOnly(2027, 3, 3)));
    }

    [Fact]
    public async Task GetDailyPricesAsync_AsksTheRepositoryFromMidnightUtcOfTheFromDate()
    {
        var repo = new Mock<IHoldingSnapshotRepository>();
        repo.Setup(r => r.ListSinceUnscopedAsync(UserId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await new HoldingSnapshotBrokeragePriceHistoryReader(repo.Object).GetDailyPricesAsync(UserId, new DateOnly(2027, 2, 8));

        repo.Verify(r => r.ListSinceUnscopedAsync(
            UserId, new DateTimeOffset(2027, 2, 8, 0, 0, 0, TimeSpan.Zero), It.IsAny<CancellationToken>()), Times.Once);
    }
}
