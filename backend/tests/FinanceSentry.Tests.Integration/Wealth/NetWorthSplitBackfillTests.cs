namespace FinanceSentry.Tests.Integration.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Infrastructure.Jobs;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

/// <summary>
/// The cash/invested split backfill of net-worth snapshots from the invested positions <c>holding_snapshots</c>
/// recorded. It runs with no person in scope (the worker's startup catch-up), against a real
/// <see cref="NetWorthSnapshotRepository"/>.
/// </summary>
public sealed class NetWorthSplitBackfillTests : IDisposable
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly FirstCapturedDay = new(2026, 7, 8);

    private readonly string _dbName = $"wealth-split-{Guid.NewGuid():N}";
    private readonly WealthDbContext _db;

    public NetWorthSplitBackfillTests()
    {
        _db = NewContext();
    }

    private WealthDbContext NewContext() => new(
        new DbContextOptionsBuilder<WealthDbContext>().UseInMemoryDatabase(_dbName).Options, new FixedCurrentUser(null));

    private static NetWorthSnapshot Row(DateOnly date, decimal banking, decimal brokerage, decimal crypto, string? stale = null, bool approximate = false) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        SnapshotDate = date,
        BankingTotal = banking,
        BrokerageTotal = brokerage,
        CryptoTotal = crypto,
        TotalNetWorth = banking + brokerage + crypto,
        Currency = "USD",
        TakenAt = DateTimeOffset.UtcNow,
        StaleSleeves = stale,
        IsApproximate = approximate,
    };

    private async Task SeedAsync(params NetWorthSnapshot[] rows)
    {
        _db.NetWorthSnapshots.AddRange(rows);
        await _db.SaveChangesAsync();
    }

    private async Task<List<NetWorthSnapshot>> StoredAsync()
    {
        await using var read = NewContext();
        return await read.NetWorthSnapshots.IgnoreQueryFilters().AsNoTracking().OrderBy(s => s.SnapshotDate).ToListAsync();
    }

    private NetWorthSplitBackfillService Backfill(params DailyInvestedBySleeve[] history)
    {
        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([UserId]);
        var invested = new Mock<IInvestedHistoryReader>();
        invested.Setup(i => i.GetDailyAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(history);
        // A fresh context, as the worker's own scope has: the seeding context's tracked rows are stale.
        return new NetWorthSplitBackfillService(banking.Object, invested.Object, new NetWorthSnapshotRepository(NewContext()));
    }

    [Fact]
    public async Task Backfill_FillsCashAsTheTotalLessTheCapturedInvestedParts_AndLeavesDaysBeforeTheHistoryNull()
    {
        await SeedAsync(
            Row(FirstCapturedDay.AddDays(-1), banking: 2_000m, brokerage: 700m, crypto: 300m),
            Row(FirstCapturedDay, banking: 2_000m, brokerage: 800m, crypto: 350m),
            Row(FirstCapturedDay.AddDays(1), banking: 2_100m, brokerage: 810m, crypto: 340m));

        var filled = await Backfill(
            new DailyInvestedBySleeve(FirstCapturedDay, BrokerageUsd: 750m, CryptoUsd: 300m),
            new DailyInvestedBySleeve(FirstCapturedDay.AddDays(1), BrokerageUsd: 760m, CryptoUsd: 290m)).BackfillAsync();

        filled.Should().Be(2);
        var rows = await StoredAsync();

        rows[0].CashTotal.Should().BeNull("a day before holding_snapshots began has no split, not zero");
        rows[0].BrokerageInvested.Should().BeNull();
        rows[0].CryptoInvested.Should().BeNull();

        rows[1].BrokerageInvested.Should().Be(750m);
        rows[1].CryptoInvested.Should().Be(300m);
        rows[1].CashTotal.Should().Be(2_100m, "bank 2000 + broker cash 50 + venue cash 50");
        rows[2].CashTotal.Should().Be(2_200m);

        foreach (var row in rows.Skip(1))
            (row.CashTotal + row.BrokerageInvested + row.CryptoInvested).Should().Be(row.TotalNetWorth);
        // Existing totals stay byte-identical.
        rows.Select(r => (r.BankingTotal, r.BrokerageTotal, r.CryptoTotal, r.TotalNetWorth)).Should().Equal(
            (2_000m, 700m, 300m, 3_000m), (2_000m, 800m, 350m, 3_150m), (2_100m, 810m, 340m, 3_250m));
    }

    [Fact]
    public async Task Backfill_RunTwice_ChangesNothingTheSecondTime()
    {
        await SeedAsync(Row(FirstCapturedDay, 2_000m, 800m, 350m));
        var sut = Backfill(new DailyInvestedBySleeve(FirstCapturedDay, 750m, 300m));

        (await sut.BackfillAsync()).Should().Be(1);
        var afterFirst = await StoredAsync();
        (await sut.BackfillAsync()).Should().Be(0);

        (await StoredAsync()).Should().BeEquivalentTo(afterFirst);
    }

    [Fact]
    public async Task Backfill_NeverOverwritesAValueAlreadyOnTheRow_AndFillsTheRestOfAPartialRow()
    {
        await SeedAsync(Row(FirstCapturedDay, 2_000m, 800m, 350m));
        await using (var tamper = NewContext())
        {
            var row = await tamper.NetWorthSnapshots.IgnoreQueryFilters().SingleAsync();
            tamper.Entry(row).Property(r => r.BrokerageInvested).CurrentValue = 111m;
            await tamper.SaveChangesAsync();
        }

        await Backfill(new DailyInvestedBySleeve(FirstCapturedDay, 750m, 300m)).BackfillAsync();

        var stored = (await StoredAsync()).Single();
        stored.BrokerageInvested.Should().Be(111m, "an existing value is never replaced");
        stored.CryptoInvested.Should().Be(300m);
        stored.CashTotal.Should().NotBeNull();
    }

    [Fact]
    public async Task Backfill_SkipsApproximateRows_AndRowsWhoseInvestedSleeveWasCarriedForward()
    {
        await SeedAsync(
            Row(FirstCapturedDay, 2_000m, 0m, 0m, approximate: true),
            Row(FirstCapturedDay.AddDays(1), 2_000m, 800m, 350m, stale: "brokerage"),
            Row(FirstCapturedDay.AddDays(2), 2_000m, 800m, 350m, stale: "banking"));

        await Backfill(
            new DailyInvestedBySleeve(FirstCapturedDay, 750m, 300m),
            new DailyInvestedBySleeve(FirstCapturedDay.AddDays(1), 750m, 300m),
            new DailyInvestedBySleeve(FirstCapturedDay.AddDays(2), 750m, 300m)).BackfillAsync();

        var rows = await StoredAsync();
        rows[0].CashTotal.Should().BeNull("a reconstructed banking-only row has nothing to split");
        rows[1].CashTotal.Should().BeNull("a carried-forward brokerage sleeve is not what the captured positions describe");
        rows[2].CashTotal.Should().NotBeNull("a carried banking balance is cash and does not affect the invested parts");
    }

    [Fact]
    public async Task Backfill_FillsRowsWhoseCarriedSleeveIsEmpty()
    {
        await SeedAsync(Row(FirstCapturedDay, 2_000m, 800m, 0m, stale: "crypto"));

        (await Backfill(new DailyInvestedBySleeve(FirstCapturedDay, 750m, 0m)).BackfillAsync()).Should().Be(1);

        var stored = (await StoredAsync()).Single();
        stored.CryptoInvested.Should().Be(0m);
        stored.CashTotal.Should().Be(2_050m);
    }

    [Fact]
    public async Task Backfill_LeavesTheSplitNull_WhenASleeveHasStoredValueButNoInvestedPositions()
    {
        await SeedAsync(
            Row(FirstCapturedDay, 2_000m, 800m, 350m),
            Row(FirstCapturedDay.AddDays(1), 2_000m, 800m, 350m),
            Row(FirstCapturedDay.AddDays(2), 2_000m, 0m, 350m));

        var filled = await Backfill(
            new DailyInvestedBySleeve(FirstCapturedDay, BrokerageUsd: 0m, CryptoUsd: 300m),
            new DailyInvestedBySleeve(FirstCapturedDay.AddDays(1), BrokerageUsd: 750m, CryptoUsd: 0m),
            new DailyInvestedBySleeve(FirstCapturedDay.AddDays(2), BrokerageUsd: 0m, CryptoUsd: 300m)).BackfillAsync();

        filled.Should().Be(1, "only the day whose empty sleeve really is empty is filled");
        var rows = await StoredAsync();
        rows[0].CashTotal.Should().BeNull("the brokerage source was missing, so its value would land whole in cash");
        rows[1].CashTotal.Should().BeNull("the crypto source was missing");
        rows[2].CashTotal.Should().Be(2_050m);
        rows[2].BrokerageInvested.Should().Be(0m);
    }

    [Fact]
    public async Task Backfill_WithNoHistory_TouchesNothing()
    {
        await SeedAsync(Row(FirstCapturedDay, 2_000m, 800m, 350m));

        (await Backfill().BackfillAsync()).Should().Be(0);

        (await StoredAsync()).Single().CashTotal.Should().BeNull();
    }

    public void Dispose() => _db.Dispose();
}
