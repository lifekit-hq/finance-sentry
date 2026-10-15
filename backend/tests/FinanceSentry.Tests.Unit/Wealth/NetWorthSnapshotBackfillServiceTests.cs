namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FinanceSentry.Modules.Wealth.Infrastructure.Jobs;
using FluentAssertions;
using Moq;
using Xunit;

public class NetWorthSnapshotBackfillServiceTests
{
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static NetWorthSnapshotJob BuildJob(List<DateOnly> capturedDates)
    {
        var bankingTotalsMock = new Mock<IBankingTotalsReader>();
        bankingTotalsMock.Setup(r => r.GetActiveUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([UserId]);
        bankingTotalsMock.Setup(r => r.GetTotalUsdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1000m);

        var cryptoMock = new Mock<ICryptoHoldingsReader>();
        cryptoMock.Setup(r => r.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var brokerageMock = new Mock<IBrokerageHoldingsReader>();
        brokerageMock.Setup(r => r.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var snapshotServiceMock = new Mock<INetWorthSnapshotService>();
        snapshotServiceMock
            .Setup(s => s.PersistSnapshotAsync(UserId, It.IsAny<NetWorthSnapshotData>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, NetWorthSnapshotData, CancellationToken>((_, data, _) => capturedDates.Add(data.SnapshotDate))
            .Returns(Task.CompletedTask);

        return new NetWorthSnapshotJob(
            bankingTotalsMock.Object,
            cryptoMock.Object,
            brokerageMock.Object,
            snapshotServiceMock.Object,
            Mock.Of<IBookFiguresService>(b => b.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(BookFigures.Empty)));
    }

    [Fact]
    public async Task BackfillAsync_FillsMissingDates_AndRefreshesStaleTodaySnapshot()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var capturedDates = new List<DateOnly>();
        var job = BuildJob(capturedDates);
        var snapshotRepositoryMock = new Mock<INetWorthSnapshotRepository>();
        snapshotRepositoryMock
            .Setup(r => r.GetLatestByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthSnapshot
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                SnapshotDate = today.AddDays(-3),
                TakenAt = DateTimeOffset.UtcNow.AddHours(-48),
            });

        var bankingTotalsMock = new Mock<IBankingTotalsReader>();
        bankingTotalsMock.Setup(r => r.GetActiveUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([UserId]);

        var sut = new NetWorthSnapshotBackfillService(
            bankingTotalsMock.Object,
            snapshotRepositoryMock.Object,
            job);

        await sut.BackfillAsync();

        capturedDates.Should().Equal(
            today.AddDays(-2),
            today.AddDays(-1),
            today);
    }

    [Fact]
    public async Task BackfillAsync_DoesNothingForFreshSnapshot()
    {
        var capturedDates = new List<DateOnly>();
        var job = BuildJob(capturedDates);
        var snapshotRepositoryMock = new Mock<INetWorthSnapshotRepository>();
        snapshotRepositoryMock
            .Setup(r => r.GetLatestByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthSnapshot
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                SnapshotDate = DateOnly.FromDateTime(DateTime.UtcNow),
                TakenAt = DateTimeOffset.UtcNow,
            });

        var bankingTotalsMock = new Mock<IBankingTotalsReader>();
        bankingTotalsMock.Setup(r => r.GetActiveUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([UserId]);

        var sut = new NetWorthSnapshotBackfillService(
            bankingTotalsMock.Object,
            snapshotRepositoryMock.Object,
            job);

        await sut.BackfillAsync();

        capturedDates.Should().BeEmpty();
    }
}
