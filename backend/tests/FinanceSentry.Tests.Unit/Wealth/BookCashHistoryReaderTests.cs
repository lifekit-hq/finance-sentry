namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Ports;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class BookCashHistoryReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 5);

    private static NetWorthSnapshot Snapshot(int dayOffset, string? staleSleeves = null)
        => new() { Id = Guid.NewGuid(), UserId = UserId, SnapshotDate = From.AddDays(dayOffset), BrokerageTotal = 999m, CryptoTotal = 999m, StaleSleeves = staleSleeves };

    private static NetWorthSnapshotSource Row(
        NetWorthSnapshot snapshot, string sourceId, decimal? total, decimal? invested, params string[] reasons)
        => new()
        {
            SnapshotId = snapshot.Id,
            SourceId = sourceId,
            Kind = sourceId == "brokerage" ? EconomyKind.Brokerage : EconomyKind.CryptoVenue,
            Total = total,
            Invested = invested,
            Health = reasons.Length > 0 ? SourceHealth.Degraded : SourceHealth.Complete,
            Reasons = reasons,
        };

    private static async Task<IReadOnlyList<DailyBookCash>> Read(
        IReadOnlyList<NetWorthSnapshot> snapshots, params NetWorthSnapshotSource[] rows)
    {
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetByUserIdUnscopedAsync(UserId, From, To, It.IsAny<CancellationToken>())).ReturnsAsync(snapshots);
        repo.Setup(r => r.GetSourcesUnscopedAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        return await new BookCashHistoryReader(repo.Object).GetDailyAsync(UserId, From, To);
    }

    [Fact]
    public async Task GetDailyAsync_CashIsTheBookTotalLessItsInvestedPart_OldestFirst_IgnoringBankingAndOtherSources()
    {
        var later = Snapshot(2);
        var earlier = Snapshot(0);

        var days = await Read(
            [later, earlier],
            Row(later, "brokerage", 150m, 120m), Row(later, "crypto", 50m, 50m),
            Row(earlier, "brokerage", 100m, 80m), Row(earlier, "crypto", 100m, 90m),
            Row(earlier, "banking", 5_000m, 0m), Row(earlier, "inzhur", 700m, 700m));

        days.Select(d => (d.Date, d.CashUsd, d.TotalUsd)).Should().Equal(
            (From, 30m, 200m),
            (From.AddDays(2), 30m, 200m));
    }

    [Fact]
    public async Task GetDailyAsync_LeavesOutADayWhoseBookSourceWasCarriedForward()
    {
        var carried = Snapshot(0);
        var measured = Snapshot(1);

        var days = await Read(
            [carried, measured],
            Row(carried, "brokerage", 100m, 100m, SourceReasons.StaleSource), Row(carried, "crypto", 50m, 40m),
            Row(measured, "brokerage", 100m, 90m), Row(measured, "crypto", 50m, 40m));

        days.Select(d => d.Date).Should().Equal(From.AddDays(1));
    }

    [Fact]
    public async Task GetDailyAsync_LeavesOutADayWithOnlyOneOfTheTwoBookSourcesSplit()
    {
        var onlyBrokerage = Snapshot(0);
        var noRows = Snapshot(1);

        var days = await Read(
            [onlyBrokerage, noRows],
            Row(onlyBrokerage, "brokerage", 100m, 80m));

        days.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyAsync_LeavesOutDaysWithNoMeasuredSplitOrNoValue()
    {
        var unsplit = Snapshot(0);
        var empty = Snapshot(1);
        var good = Snapshot(2);

        var days = await Read(
            [unsplit, empty, good],
            Row(unsplit, "brokerage", 100m, null), Row(unsplit, "crypto", 50m, 40m),
            Row(empty, "brokerage", 0m, 0m), Row(empty, "crypto", 0m, 0m),
            Row(good, "brokerage", 100m, 100m), Row(good, "crypto", 100m, 100m));

        days.Should().ContainSingle().Which.Should().Be(new DailyBookCash(From.AddDays(2), 0m, 200m));
    }
}
