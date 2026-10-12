namespace FinanceSentry.Tests.Integration.Wealth;

using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>584: InsertMissingAsync is what makes the net-worth backfill idempotent and
/// safe to re-run without ever overwriting a real (non-backfilled) snapshot. Both writes are
/// <c>INSERT ... ON CONFLICT</c> on the unique (UserId, SnapshotDate) index, which the in-memory
/// provider neither enforces nor runs, so this needs real Postgres.</summary>
[Trait("Category", "Integration")]
public sealed class NetWorthSnapshotRepositoryInsertMissingTests : IAsyncLifetime
{
    private static readonly Guid UserId = Guid.NewGuid();
    private TestDatabase? _database;
    private WealthDbContext _db = null!;
    private NetWorthSnapshotRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();
        _db = CreateContext();
        await _db.Database.EnsureCreatedAsync();
        _repo = new NetWorthSnapshotRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        if (_database is not null)
            await _database.DisposeAsync();
    }

    // The backfill command runs as the person it backfills.
    private WealthDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WealthDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(UserId));

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

    [DockerRequiredFact]
    public async Task InsertMissingAsync_ReRunWithSameRows_InsertsNothingTheSecondTime()
    {
        var rows = new[] { MakeRow(new DateOnly(2026, 6, 1), 100m), MakeRow(new DateOnly(2026, 6, 2), 110m) };

        var firstRun = await _repo.InsertMissingAsync(rows, CancellationToken.None);
        var secondRun = await _repo.InsertMissingAsync(rows, CancellationToken.None);

        firstRun.Should().Be(2);
        secondRun.Should().Be(0);
        (await _db.NetWorthSnapshots.CountAsync()).Should().Be(2);
    }

    [DockerRequiredFact]
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

    [DockerRequiredFact]
    public async Task UpsertAsync_RepeatedForOneDay_KeepsOneRowAndUpdatesIt()
    {
        var date = new DateOnly(2026, 6, 5);
        var first = MakeRow(date, bankingTotal: 100m, approximate: true);
        await _repo.UpsertAsync(first, CancellationToken.None);

        var second = MakeRow(date, bankingTotal: 250m, approximate: false);
        await using (var ctx = CreateContext())
            await new NetWorthSnapshotRepository(ctx).UpsertAsync(second, CancellationToken.None);

        await using var read = CreateContext();
        var row = await read.NetWorthSnapshots.AsNoTracking().SingleAsync(s => s.SnapshotDate == date);
        row.Id.Should().Be(first.Id, "the day's row is updated in place");
        row.BankingTotal.Should().Be(250m);
        row.TotalNetWorth.Should().Be(250m);
        row.IsApproximate.Should().BeFalse();
    }

    [DockerRequiredFact]
    public async Task UpsertAsync_Concurrent_LeavesOneRowWithoutFailing()
    {
        var date = new DateOnly(2026, 6, 6);
        var writes = Enumerable.Range(1, 8).Select(async i =>
        {
            await using var ctx = CreateContext();
            await new NetWorthSnapshotRepository(ctx).UpsertAsync(MakeRow(date, bankingTotal: i * 10m), CancellationToken.None);
        });

        await Task.WhenAll(writes);

        await using var read = CreateContext();
        (await read.NetWorthSnapshots.CountAsync(s => s.SnapshotDate == date)).Should().Be(1);
    }

    [DockerRequiredFact]
    public async Task InsertMissingAsync_Concurrent_InsertsEachDayOnce()
    {
        var rows = new[] { new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2) };
        var inserted = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var ctx = CreateContext();
            return await new NetWorthSnapshotRepository(ctx)
                .InsertMissingAsync(rows.Select(d => MakeRow(d, 100m)).ToList(), CancellationToken.None);
        }));

        inserted.Sum().Should().Be(2);
        await using var read = CreateContext();
        (await read.NetWorthSnapshots.CountAsync()).Should().Be(2);
    }
}
