namespace FinanceSentry.Tests.Integration.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

/// <summary>
/// The per-source snapshot rows (framework §5.6, step 2) against real Postgres: the migration is purely additive,
/// the day's source rows are replaced together with the snapshot row, and deleting a snapshot takes its rows.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NetWorthSnapshotSourcesTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261015120000_M007_AddNetWorthSnapshotFxStatus";
    private const string SourcesMigration = "20261015130000_M008_AddNetWorthSnapshotSources";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 15);

    private TestDatabase? _database;

    public async Task InitializeAsync() => _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private WealthDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<WealthDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    private static NetWorthSnapshot Snapshot(decimal banking, decimal brokerage, decimal crypto) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        SnapshotDate = Day,
        BankingTotal = banking,
        BrokerageTotal = brokerage,
        CryptoTotal = crypto,
        TotalNetWorth = banking + brokerage + crypto,
        Currency = "USD",
        TakenAt = DateTimeOffset.UtcNow,
    };

    private static NetWorthSnapshotSource Row(string sourceId, EconomyKind kind, decimal? total, SourceHealth health = SourceHealth.Complete, params string[] reasons) => new()
    {
        SourceId = sourceId,
        Kind = kind,
        Total = total,
        Cash = total is null ? null : 0m,
        Invested = total,
        Health = health,
        Reasons = reasons,
    };

    private async Task<bool> TableExistsAsync()
    {
        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT to_regclass('public.net_worth_snapshot_sources') IS NOT NULL", conn);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    [DockerRequiredFact]
    public async Task Up_OnAnEmptyDatabase_CreatesTheTable_AndDownDropsIt()
    {
        await using (var ctx = CreateContext())
            await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        (await TableExistsAsync()).Should().BeFalse();

        await using (var ctx = CreateContext())
            await ctx.GetService<IMigrator>().MigrateAsync(SourcesMigration);
        (await TableExistsAsync()).Should().BeTrue();

        await using (var ctx = CreateContext())
            await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        (await TableExistsAsync()).Should().BeFalse();
    }

    [DockerRequiredFact]
    public async Task Up_OnAPopulatedDatabase_LeavesExistingSnapshotsUntouchedAndWithoutSourceRows()
    {
        await using (var ctx = CreateContext())
        {
            await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await ctx.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO net_worth_snapshots ("Id", "UserId", "SnapshotDate", "BankingTotal", "BrokerageTotal", "CryptoTotal", "TotalNetWorth", "Currency", "TakenAt")
                VALUES ({Guid.NewGuid()}, {UserId}, DATE '2026-07-01', 1000.10, 300.20, 50.30, 1350.60, 'USD', NOW())
                """);
        }

        await using (var ctx = CreateContext())
            await ctx.GetService<IMigrator>().MigrateAsync(SourcesMigration);

        await using var read = CreateContext();
        var snapshot = await read.NetWorthSnapshots.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.UserId == UserId);
        snapshot.TotalNetWorth.Should().Be(1350.60m);
        (await read.NetWorthSnapshotSources.IgnoreQueryFilters().AnyAsync()).Should().BeFalse("past snapshots are not backfilled");
    }

    [DockerRequiredFact]
    public async Task UpsertWithSources_WritesTheRowsAndTheirSumsMatchTheSnapshotColumns()
    {
        await using (var ctx = CreateContext())
            await ctx.Database.MigrateAsync();

        await using (var ctx = CreateContext())
        {
            await new NetWorthSnapshotRepository(ctx).UpsertWithSourcesAsync(
                Snapshot(1_000m, 210m, 50m),
                [
                    Row("banking", EconomyKind.Bank, 1_000m),
                    Row("brokerage", EconomyKind.Brokerage, 210m),
                    Row("crypto", EconomyKind.CryptoVenue, 50m, SourceHealth.Estimated, "FX_TODAY_RATE"),
                    Row("fund", EconomyKind.Fund, null, SourceHealth.Unavailable, "STALE_SOURCE"),
                ]);
        }

        await using var read = CreateContext(UserId);
        var snapshot = await read.NetWorthSnapshots.SingleAsync();
        var rows = await read.NetWorthSnapshotSources.OrderBy(r => r.SourceId).ToListAsync();

        rows.Should().HaveCount(4);
        rows.Sum(r => r.Total ?? 0m).Should().Be(snapshot.BankingTotal + snapshot.BrokerageTotal + snapshot.CryptoTotal);
        var crypto = rows.Single(r => r.SourceId == "crypto");
        (crypto.Kind, crypto.Health).Should().Be((EconomyKind.CryptoVenue, SourceHealth.Estimated));
        crypto.Reasons.Should().Equal("FX_TODAY_RATE");
        var fund = rows.Single(r => r.SourceId == "fund");
        fund.Total.Should().BeNull();
        fund.Health.Should().Be(SourceHealth.Unavailable);
        rows.Should().OnlyContain(r => r.SnapshotId == snapshot.Id);
    }

    [DockerRequiredFact]
    public async Task UpsertWithSources_OnASecondRunOfTheSameDay_ReplacesTheRowsAndKeepsTheSnapshotId()
    {
        await using (var ctx = CreateContext())
            await ctx.Database.MigrateAsync();

        Guid firstId;
        await using (var ctx = CreateContext())
        {
            var first = Snapshot(1_000m, 0m, 0m);
            firstId = first.Id;
            await new NetWorthSnapshotRepository(ctx).UpsertWithSourcesAsync(
                first, [Row("banking", EconomyKind.Bank, 1_000m), Row("crypto", EconomyKind.CryptoVenue, 0m)]);
        }

        await using (var ctx = CreateContext())
            await new NetWorthSnapshotRepository(ctx).UpsertWithSourcesAsync(
                Snapshot(1_200m, 0m, 0m), [Row("banking", EconomyKind.Bank, 1_200m)]);

        await using var read = CreateContext(UserId);
        var snapshot = await read.NetWorthSnapshots.SingleAsync();
        snapshot.Id.Should().Be(firstId);
        snapshot.BankingTotal.Should().Be(1_200m);
        var rows = await read.NetWorthSnapshotSources.ToListAsync();
        rows.Should().ContainSingle().Which.Total.Should().Be(1_200m);
        rows.Single().SnapshotId.Should().Be(firstId);
    }

    [DockerRequiredFact]
    public async Task DeletingASnapshot_CascadesToItsSourceRows()
    {
        await using (var ctx = CreateContext())
            await ctx.Database.MigrateAsync();

        await using (var ctx = CreateContext())
            await new NetWorthSnapshotRepository(ctx).UpsertWithSourcesAsync(
                Snapshot(1_000m, 0m, 0m), [Row("banking", EconomyKind.Bank, 1_000m)]);

        await using var del = CreateContext(UserId);
        await del.NetWorthSnapshots.ExecuteDeleteAsync();

        await using var read = CreateContext();
        (await read.NetWorthSnapshotSources.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [DockerRequiredFact]
    public async Task TheSameSourceTwiceForOneSnapshot_IsRejectedByTheUniqueIndex()
    {
        await using (var ctx = CreateContext())
            await ctx.Database.MigrateAsync();

        await using var write = CreateContext();
        var act = () => new NetWorthSnapshotRepository(write).UpsertWithSourcesAsync(
            Snapshot(1_000m, 0m, 0m), [Row("banking", EconomyKind.Bank, 1m), Row("banking", EconomyKind.Bank, 2m)]);

        await act.Should().ThrowAsync<Exception>();
    }
}
