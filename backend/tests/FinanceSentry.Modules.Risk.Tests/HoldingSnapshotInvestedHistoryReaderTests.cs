using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Modules.Risk.Tests;

public sealed class HoldingSnapshotInvestedHistoryReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static HoldingSnapshot Snap(DateTimeOffset at, string sleeve, decimal usd)
        => new() { UserId = UserId, Symbol = "X", Sleeve = sleeve, Quantity = 1m, UsdValue = usd, CapturedAt = at };

    private static HoldingSnapshotInvestedHistoryReader ReaderOver(params HoldingSnapshot[] rows)
    {
        var repo = new Mock<IHoldingSnapshotRepository>();
        repo.Setup(r => r.ListSinceUnscopedAsync(UserId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        return new HoldingSnapshotInvestedHistoryReader(repo.Object);
    }

    [Fact]
    public async Task GetDailyAsync_SumsEachSleeveOfTheLatestCaptureOfTheUtcDay()
    {
        var early = new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero);
        var late = new DateTimeOffset(2026, 8, 1, 22, 0, 0, TimeSpan.Zero);
        var reader = ReaderOver(
            Snap(early, RiskSleeve.Brokerage, 900m),
            Snap(late, RiskSleeve.Brokerage, 600m),
            Snap(late, RiskSleeve.Brokerage, 400m),
            Snap(late, RiskSleeve.Crypto, 250m));

        var days = await reader.GetDailyAsync(UserId, new DateOnly(2026, 7, 1));

        days.Should().ContainSingle().Which.Should().Be(new FinanceSentry.Core.Interfaces.DailyInvestedBySleeve(
            new DateOnly(2026, 8, 1), 1_000m, 250m));
    }

    [Fact]
    public async Task GetDailyAsync_ReturnsOneEntryPerCapturedDay_OrderedByDate_AndOmitsDaysWithoutACapture()
    {
        var reader = ReaderOver(
            Snap(new DateTimeOffset(2026, 8, 3, 2, 0, 0, TimeSpan.Zero), RiskSleeve.Crypto, 30m),
            Snap(new DateTimeOffset(2026, 8, 1, 2, 0, 0, TimeSpan.Zero), RiskSleeve.Brokerage, 10m));

        var days = await reader.GetDailyAsync(UserId, new DateOnly(2026, 7, 1));

        days.Select(d => d.Date).Should().Equal(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 3));
        days[0].CryptoUsd.Should().Be(0m);
        days[1].BrokerageUsd.Should().Be(0m);
    }

    [Fact]
    public async Task GetDailyAsync_WithNoHistory_IsEmpty()
    {
        (await ReaderOver().GetDailyAsync(UserId, new DateOnly(2026, 7, 1))).Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyAsync_AsksTheRepositoryFromMidnightUtcOfTheFromDate()
    {
        var repo = new Mock<IHoldingSnapshotRepository>();
        repo.Setup(r => r.ListSinceUnscopedAsync(UserId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await new HoldingSnapshotInvestedHistoryReader(repo.Object).GetDailyAsync(UserId, new DateOnly(2026, 7, 8));

        repo.Verify(r => r.ListSinceUnscopedAsync(
            UserId, new DateTimeOffset(2026, 7, 8, 0, 0, 0, TimeSpan.Zero), It.IsAny<CancellationToken>()), Times.Once);
    }
}
