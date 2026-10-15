namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class BrokerageValueHistoryReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 2);

    [Fact]
    public async Task GetDailyAsync_MarksADayStale_OnlyWhenItsBrokerageSourceIs_FromRowsOrTheLegacyColumn()
    {
        // Day one predates per-source rows (the column names brokerage); day two has rows marking crypto and
        // another source stale, which leave the brokerage figure untouched.
        var old = new NetWorthSnapshot { Id = Guid.NewGuid(), UserId = UserId, SnapshotDate = From, BrokerageTotal = 100m, StaleSleeves = "brokerage" };
        var current = new NetWorthSnapshot { Id = Guid.NewGuid(), UserId = UserId, SnapshotDate = To, BrokerageTotal = 110m, CryptoTotal = 5m };
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetByUserIdUnscopedAsync(UserId, From, To, It.IsAny<CancellationToken>())).ReturnsAsync([old, current]);
        repo.Setup(r => r.GetSourcesUnscopedAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new NetWorthSnapshotSource { SnapshotId = current.Id, SourceId = "brokerage", Kind = EconomyKind.Brokerage, Total = 110m },
                new NetWorthSnapshotSource { SnapshotId = current.Id, SourceId = "crypto", Kind = EconomyKind.CryptoVenue, Total = 0m, Health = SourceHealth.Degraded, Reasons = [SourceReasons.StaleSource] },
                new NetWorthSnapshotSource { SnapshotId = current.Id, SourceId = "inzhur", Kind = EconomyKind.Fund, Health = SourceHealth.Unavailable, Reasons = [SourceReasons.StaleSource] },
            ]);

        var days = await new BrokerageValueHistoryReader(repo.Object).GetDailyAsync(UserId, From, To);

        days.Select(d => d.BrokerageTotalUsd).Should().Equal(100m, 110m);
        days[0].Signals!.StaleSources.Should().Equal("brokerage");
        days[1].Signals!.StaleSources.Should().BeEmpty();
    }
}
