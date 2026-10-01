namespace FinanceSentry.Tests.Integration.Wealth;

using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>584: InsertMissingAsync is what makes the net-worth backfill idempotent and
/// safe to re-run without ever overwriting a real (non-backfilled) snapshot.</summary>
public sealed class NetWorthSnapshotRepositoryInsertMissingTests : IDisposable
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly WealthDbContext _db;
    private readonly NetWorthSnapshotRepository _repo;

    public NetWorthSnapshotRepositoryInsertMissingTests()
    {
        // The backfill command runs as the person it backfills.
        _db = new WealthDbContext(new DbContextOptionsBuilder<WealthDbContext>()
            .UseInMemoryDatabase($"wealth-{Guid.NewGuid():N}").Options, new FixedCurrentUser(UserId));
        _repo = new NetWorthSnapshotRepository(_db);
    }

    private static NetWorthSnapshot MakeRow(DateOnly date, decimal bankingTotal, bool approximate = true) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        SnapshotDate = date,
        BankingTotal = bankingTotal,
        BrokerageTotal = 0m,
        CryptoTotal = 0m,
        TotalNetWorth = bankingTotal,
        Currency = "USD",
        TakenAt = DateTimeOffset.UtcNow,
        IsApproximate = approximate,
    };

    [Fact]
    public async Task InsertMissingAsync_ReRunWithSameRows_InsertsNothingTheSecondTime()
    {
        var rows = new[] { MakeRow(new DateOnly(2026, 6, 1), 100m), MakeRow(new DateOnly(2026, 6, 2), 110m) };

        var firstRun = await _repo.InsertMissingAsync(rows, CancellationToken.None);
        var secondRun = await _repo.InsertMissingAsync(rows, CancellationToken.None);

        firstRun.Should().Be(2);
        secondRun.Should().Be(0);
        (await _db.NetWorthSnapshots.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task InsertMissingAsync_NeverOverwritesAPreExistingRealSnapshot()
    {
        var realSnapshot = MakeRow(new DateOnly(2026, 6, 30), bankingTotal: 5000m, approximate: false);
        _db.NetWorthSnapshots.Add(realSnapshot);
        await _db.SaveChangesAsync();

        var backfillAttempt = new[] { MakeRow(new DateOnly(2026, 6, 30), bankingTotal: 1m) };
        var inserted = await _repo.InsertMissingAsync(backfillAttempt, CancellationToken.None);

        inserted.Should().Be(0);
        var stored = await _db.NetWorthSnapshots.SingleAsync(s => s.SnapshotDate == new DateOnly(2026, 6, 30));
        stored.BankingTotal.Should().Be(5000m);
        stored.IsApproximate.Should().BeFalse();
    }

    public void Dispose() => _db.Dispose();
}
