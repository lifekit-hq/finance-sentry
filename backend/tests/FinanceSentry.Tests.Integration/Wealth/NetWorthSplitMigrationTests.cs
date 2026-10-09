namespace FinanceSentry.Tests.Integration.Wealth;

using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

/// <summary>
/// M004_AddNetWorthSnapshotCashSplit against real Postgres: it only adds three nullable columns, so rows written
/// before it keep their totals byte-for-byte and read back with a null split (never zero), and Down removes the
/// columns again.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NetWorthSplitMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260928141916_M003_AddNetWorthSnapshotIsApproximate";
    private const string SplitMigration = "20261009141125_M004_AddNetWorthSnapshotCashSplit";
    private static readonly string[] SplitColumns = ["cash_total", "brokerage_invested", "crypto_invested"];

    private TestDatabase? _database;

    public async Task InitializeAsync() => _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private WealthDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WealthDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private async Task<List<string>> SplitColumnsPresentAsync()
    {
        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'net_worth_snapshots' AND column_name = ANY(@cols) ORDER BY column_name",
            conn);
        cmd.Parameters.AddWithValue("cols", SplitColumns);
        var found = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            found.Add(reader.GetString(0));
        return found;
    }

    [DockerRequiredFact]
    public async Task Up_AddsNullableColumns_ExistingRowsKeepTheirTotalsAndReadBackWithoutASplit()
    {
        var userId = Guid.NewGuid();
        await using (var ctx = CreateContext())
        {
            await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await ctx.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO net_worth_snapshots ("Id", "UserId", "SnapshotDate", "BankingTotal", "BrokerageTotal", "CryptoTotal", "TotalNetWorth", "Currency", "TakenAt")
                VALUES ({Guid.NewGuid()}, {userId}, DATE '2026-07-01', 1000.10, 300.20, 50.30, 1350.60, 'USD', NOW())
                """);
        }
        (await SplitColumnsPresentAsync()).Should().BeEmpty();

        await using (var ctx = CreateContext())
            await ctx.GetService<IMigrator>().MigrateAsync(SplitMigration);

        (await SplitColumnsPresentAsync()).Should().BeEquivalentTo(SplitColumns);
        await using var read = CreateContext();
        var row = await read.NetWorthSnapshots.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.UserId == userId);
        (row.BankingTotal, row.BrokerageTotal, row.CryptoTotal, row.TotalNetWorth).Should().Be((1000.10m, 300.20m, 50.30m, 1350.60m));
        row.CashTotal.Should().BeNull();
        row.BrokerageInvested.Should().BeNull();
        row.CryptoInvested.Should().BeNull();
    }

    [DockerRequiredFact]
    public async Task Down_DropsTheSplitColumns_AndKeepsTheRows()
    {
        var userId = Guid.NewGuid();
        await using (var ctx = CreateContext())
        {
            await ctx.GetService<IMigrator>().MigrateAsync(SplitMigration);
            await ctx.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO net_worth_snapshots ("Id", "UserId", "SnapshotDate", "BankingTotal", "BrokerageTotal", "CryptoTotal", "TotalNetWorth", "Currency", "TakenAt", cash_total, brokerage_invested, crypto_invested)
                VALUES ({Guid.NewGuid()}, {userId}, DATE '2026-10-01', 1000, 300, 50, 1350, 'USD', NOW(), 1100, 200, 50)
                """);
        }
        (await SplitColumnsPresentAsync()).Should().BeEquivalentTo(SplitColumns);

        await using (var ctx = CreateContext())
            await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        (await SplitColumnsPresentAsync()).Should().BeEmpty();
        await using var read = CreateContext();
        (await read.Database.SqlQuery<decimal>($"SELECT \"TotalNetWorth\" AS \"Value\" FROM net_worth_snapshots WHERE \"UserId\" = {userId}").ToListAsync())
            .Should().Equal(1350m);
    }
}
