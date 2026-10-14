namespace FinanceSentry.Tests.Integration.Wealth;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Migrations;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

/// <summary>
/// M006_RestoreRowsBeforeInzhurInflation against real Postgres, on the production shape that M005 over-corrected: an
/// owner whose Inzhur credential existed from 2026-10-06 20:00Z but whose holdings first reached the net-worth
/// snapshots, 100x inflated, on 2026-10-12 (brokerage 10,708.84 -> 13,208.84). M005 took 99x the ledger-derived
/// value ($1,980 invested + $495 cash at 40 UAH/USD) off every row from 10-07, including the five days that never
/// held any Inzhur value. M006 puts those five back and keeps 10-12 / 10-13 corrected.
/// </summary>
[Trait("Category", "Integration")]
public sealed class InzhurHistoryRestoreMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261014120000_M005_CorrectInzhurNetWorthHistory";
    private const string RestoreMigration = "20261014190000_M006_RestoreRowsBeforeInzhurInflation";

    private static readonly DateTimeOffset FixedSyncAt = new(2026, 10, 13, 7, 0, 5, TimeSpan.Zero);

    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _inflatedFromFirstRow = Guid.NewGuid();
    private readonly Guid _neverInflated = Guid.NewGuid();
    private readonly Guid _ibkrOnly = Guid.NewGuid();
    private TestDatabase? _database;

    public async Task InitializeAsync() => _database = await PostgresServer.Postgres14.CreateDatabaseAsync();

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private WealthDbContext WealthContext() =>
        new(new DbContextOptionsBuilder<WealthDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private BrokerageSyncDbContext BrokerageContext() =>
        new(new DbContextOptionsBuilder<BrokerageSyncDbContext>()
            .UseNpgsql(_database!.ConnectionString, b => b.MigrationsHistoryTable("__EFMigrationsHistory", "public"))
            .Options, NoCurrentUser.Instance);

    private sealed record SnapshotRow(
        Guid UserId, DateOnly Date, decimal Brokerage, decimal Total, decimal? Cash, decimal? BrokerageInvested);

    private async Task MigrateAsync(string wealthTarget)
    {
        await using (var brokerage = BrokerageContext())
            await brokerage.Database.MigrateAsync();
        await using var wealth = WealthContext();
        await wealth.GetService<IMigrator>().MigrateAsync(wealthTarget);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedAsync()
    {
        await ExecuteAsync($$"""
            INSERT INTO brokerage_sync."InzhurCredentials"
                ("Id", "UserId", "Status", "CreatedAt", "LastSyncAt", "EncryptedSession", "SessionIv", "SessionAuthTag",
                 "Health_State", "Health_ConsecutiveFailures")
            VALUES
                (gen_random_uuid(), '{{_owner}}', 'Active', TIMESTAMPTZ '2026-10-06 20:00:00Z', TIMESTAMPTZ '2026-10-14 07:00:04Z', '\x00', '\x00', '\x00', 'Healthy', 0),
                (gen_random_uuid(), '{{_inflatedFromFirstRow}}', 'Active', TIMESTAMPTZ '2026-10-08 06:00:00Z', TIMESTAMPTZ '2026-10-14 07:00:04Z', '\x00', '\x00', '\x00', 'Healthy', 0),
                (gen_random_uuid(), '{{_neverInflated}}', 'Active', TIMESTAMPTZ '2026-10-07 00:00:00Z', TIMESTAMPTZ '2026-10-14 07:00:04Z', '\x00', '\x00', '\x00', 'Healthy', 0);

            -- Corrected holdings at 40 UAH/USD: one fund ($20.00) and UAH cash ($5.00) for the owner.
            INSERT INTO brokerage_sync."BrokerageHoldings"
                ("Id", "UserId", "Symbol", "InstrumentType", "Quantity", "UsdValue", "SyncedAt", "Provider", "Currency", "NativeValue")
            VALUES
                (gen_random_uuid(), '{{_owner}}', 'Inzhur MilTech', 'FUND', 2, 20.00, TIMESTAMPTZ '2026-10-14 07:00:04Z', 'inzhur', 'UAH', 800),
                (gen_random_uuid(), '{{_owner}}', 'UAH Cash', 'CASH', 200, 5.00, TIMESTAMPTZ '2026-10-14 07:00:04Z', 'inzhur', 'UAH', 200),
                (gen_random_uuid(), '{{_owner}}', 'VWRA', 'STK', 100, 10408.84, TIMESTAMPTZ '2026-10-14 06:00:00Z', 'ibkr', 'USD', NULL),
                (gen_random_uuid(), '{{_inflatedFromFirstRow}}', 'Inzhur REIT', 'FUND', 10, 2.8875, TIMESTAMPTZ '2026-10-14 07:00:04Z', 'inzhur', 'UAH', 115.50),
                (gen_random_uuid(), '{{_neverInflated}}', 'Inzhur REIT', 'FUND', 10, 2.8875, TIMESTAMPTZ '2026-10-14 07:00:04Z', 'inzhur', 'UAH', 115.50);

            -- Each fixed build's first sync imported the ledger (CreatedAt); every trade predates the snapshots.
            INSERT INTO brokerage_sync."BrokerageTrades"
                ("Id", "UserId", "Provider", "IbExecutionId", "IbTradeId", "Symbol", "TradeDateTime", "Quantity", "Price",
                 "Proceeds", "Currency", "CreatedAt", "UpdatedAt")
            VALUES
                (gen_random_uuid(), '{{_owner}}', 'inzhur', 'o-1', 'o-1', 'Inzhur MilTech', TIMESTAMPTZ '2025-11-03 09:00:00Z', 2, 400, -800, 'UAH', TIMESTAMPTZ '{{FixedSyncAt:O}}', TIMESTAMPTZ '{{FixedSyncAt:O}}'),
                (gen_random_uuid(), '{{_inflatedFromFirstRow}}', 'inzhur', 'e-1', 'e-1', 'Inzhur REIT', TIMESTAMPTZ '2026-10-01 09:00:00Z', 10, 11, -110, 'UAH', TIMESTAMPTZ '2026-10-13 12:00:30Z', TIMESTAMPTZ '2026-10-13 12:00:30Z'),
                (gen_random_uuid(), '{{_neverInflated}}', 'inzhur', 'n-1', 'n-1', 'Inzhur REIT', TIMESTAMPTZ '2026-10-01 09:00:00Z', 10, 11, -110, 'UAH', TIMESTAMPTZ '2026-10-13 12:00:30Z', TIMESTAMPTZ '2026-10-13 12:00:30Z');

            -- The owner: before the credential (10-06), five days that never held Inzhur, the inflated 10-12 and 10-13
            -- rows (10-13 without a split), and the first row from the corrected holdings (10-14).
            INSERT INTO net_worth_snapshots
                ("Id", "UserId", "SnapshotDate", "BankingTotal", "BrokerageTotal", "CryptoTotal", "TotalNetWorth", "Currency",
                 "TakenAt", cash_total, brokerage_invested, crypto_invested)
            VALUES
                (gen_random_uuid(), '{{_owner}}', DATE '2026-10-06', 5000, 11075.28, 1000, 17075.28, 'USD', TIMESTAMPTZ '2026-10-06 01:00:00Z', 5300.00, 10775.28, 1000),
                (gen_random_uuid(), '{{_owner}}', DATE '2026-10-07', 5000, 10678.96, 1000, 16678.96, 'USD', TIMESTAMPTZ '2026-10-07 01:00:00Z', 5300.00, 10378.96, 1000),
                (gen_random_uuid(), '{{_owner}}', DATE '2026-10-08', 5000, 10912.40, 1000, 16912.40, 'USD', TIMESTAMPTZ '2026-10-08 01:00:00Z', 5300.00, 10612.40, 1000),
                (gen_random_uuid(), '{{_owner}}', DATE '2026-10-09', 5000, 11130.84, 1000, 17130.84, 'USD', TIMESTAMPTZ '2026-10-09 01:00:00Z', 5300.00, 10830.84, 1000),
                (gen_random_uuid(), '{{_owner}}', DATE '2026-10-10', 5000, 10845.10, 1000, 16845.10, 'USD', TIMESTAMPTZ '2026-10-10 01:00:00Z', 5300.00, 10545.10, 1000),
                (gen_random_uuid(), '{{_owner}}', DATE '2026-10-11', 5000, 10708.84, 1000, 16708.84, 'USD', TIMESTAMPTZ '2026-10-11 01:00:00Z', 5300.00, 10408.84, 1000),
                (gen_random_uuid(), '{{_owner}}', DATE '2026-10-12', 5000, 13208.84, 1000, 19208.84, 'USD', TIMESTAMPTZ '2026-10-12 18:00:00Z', 5800.00, 12408.84, 1000),
                (gen_random_uuid(), '{{_owner}}', DATE '2026-10-13', 5000, 13250.00, 1000, 19250.00, 'USD', TIMESTAMPTZ '2026-10-13 01:00:00Z', NULL, NULL, NULL),
                (gen_random_uuid(), '{{_owner}}', DATE '2026-10-14', 5000, 10760.00, 1000, 16760.00, 'USD', TIMESTAMPTZ '2026-10-14 01:00:00Z', 5360.00, 10460.00, 1000),
                (gen_random_uuid(), '{{_ibkrOnly}}', DATE '2026-10-09', 2000, 7000.00, 0, 9000.00, 'USD', TIMESTAMPTZ '2026-10-09 18:00:00Z', 2000.00, 7000.00, 0);

            -- First-ever row already inflated: nothing precedes it, so there is no evidence it was ever clean.
            INSERT INTO net_worth_snapshots
                ("Id", "UserId", "SnapshotDate", "BankingTotal", "BrokerageTotal", "CryptoTotal", "TotalNetWorth", "Currency",
                 "TakenAt", stale_sleeves, cash_total, brokerage_invested, crypto_invested)
            VALUES
                (gen_random_uuid(), '{{_inflatedFromFirstRow}}', DATE '2026-10-08', 300, 288.75, 0, 588.75, 'USD', TIMESTAMPTZ '2026-10-08 07:00:00Z', NULL, 300.00, 288.75, 0),
                (gen_random_uuid(), '{{_inflatedFromFirstRow}}', DATE '2026-10-09', 310, 288.75, 0, 598.75, 'USD', TIMESTAMPTZ '2026-10-09 01:00:00Z', 'brokerage', 310.00, 288.75, 0);

            -- Brokerage moves a little each day and never steps by anything near the removal: no onset to anchor on.
            INSERT INTO net_worth_snapshots
                ("Id", "UserId", "SnapshotDate", "BankingTotal", "BrokerageTotal", "CryptoTotal", "TotalNetWorth", "Currency",
                 "TakenAt", cash_total, brokerage_invested, crypto_invested)
            VALUES
                (gen_random_uuid(), '{{_neverInflated}}', DATE '2026-10-06', 400, 990.00, 0, 1390.00, 'USD', TIMESTAMPTZ '2026-10-06 01:00:00Z', 400.00, 990.00, 0),
                (gen_random_uuid(), '{{_neverInflated}}', DATE '2026-10-08', 400, 1000.00, 0, 1400.00, 'USD', TIMESTAMPTZ '2026-10-08 01:00:00Z', 400.00, 1000.00, 0),
                (gen_random_uuid(), '{{_neverInflated}}', DATE '2026-10-09', 400, 1010.00, 0, 1410.00, 'USD', TIMESTAMPTZ '2026-10-09 01:00:00Z', 400.00, 1010.00, 0);
            """);
    }

    private async Task<List<SnapshotRow>> SnapshotsAsync()
    {
        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT "UserId", "SnapshotDate", "BrokerageTotal", "TotalNetWorth", cash_total, brokerage_invested
            FROM net_worth_snapshots ORDER BY "UserId", "SnapshotDate", "TakenAt"
            """, conn);
        var rows = new List<SnapshotRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new SnapshotRow(reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1), reader.GetDecimal(2),
                reader.GetDecimal(3), NullableDecimal(reader, 4), NullableDecimal(reader, 5)));
        }
        return rows;
    }

    private async Task<int> BackupCountAsync()
    {
        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT count(*) FROM {M005_CorrectInzhurNetWorthHistory.BackupTable}", conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static decimal? NullableDecimal(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private static List<SnapshotRow> ForUser(IEnumerable<SnapshotRow> rows, Guid userId) =>
        rows.Where(r => r.UserId == userId).ToList();

    private static SnapshotRow Row(IEnumerable<SnapshotRow> rows, Guid userId, int day) =>
        rows.Single(r => r.UserId == userId && r.Date == new DateOnly(2026, 10, day));

    private static readonly int[] CleanDays = [7, 8, 9, 10, 11];

    [DockerRequiredFact]
    public async Task M005_DipsTheCleanDays_AndM006RestoresThemToTheirBackedUpValues()
    {
        await MigrateAsync("20261009141125_M004_AddNetWorthSnapshotCashSplit");
        await SeedAsync();
        var original = await SnapshotsAsync();
        await MigrateAsync(PreviousMigration);
        var afterM005 = await SnapshotsAsync();
        // The false dip M006 exists for: 2,475 off a row that never held Inzhur.
        Row(afterM005, _owner, 9).Brokerage.Should().Be(11130.84m - 2475.00m);

        await MigrateAsync(RestoreMigration);

        var restored = await SnapshotsAsync();
        foreach (var day in CleanDays)
            Row(restored, _owner, day).Should().Be(Row(original, _owner, day));
        (await BackupCountAsync()).Should().Be(11, "M006 keeps the backup table, and every row in it");
    }

    [DockerRequiredFact]
    public async Task M006_KeepsTheInflatedRowsCorrected_AndLeavesRowsM005NeverTouched()
    {
        await MigrateAsync("20261009141125_M004_AddNetWorthSnapshotCashSplit");
        await SeedAsync();
        var original = await SnapshotsAsync();
        await MigrateAsync(PreviousMigration);
        var afterM005 = await SnapshotsAsync();

        await MigrateAsync(RestoreMigration);

        var restored = await SnapshotsAsync();
        // 10-12 held the 100x value (+2,500 on the day); 99x the ledger value is off, 25.00 of real Inzhur remains.
        Row(restored, _owner, 12).Should().Be(
            new SnapshotRow(_owner, new DateOnly(2026, 10, 12), 10733.84m, 16733.84m, 5305.00m, 10428.84m));
        Row(restored, _owner, 13).Should().Be(Row(afterM005, _owner, 13));
        Row(restored, _owner, 13).Brokerage.Should().Be(13250.00m - 2475.00m);
        foreach (var day in new[] { 6, 14 })
            Row(restored, _owner, day).Should().Be(Row(original, _owner, day));
        Row(restored, _ibkrOnly, 9).Should().Be(Row(original, _ibkrOnly, 9));
    }

    [DockerRequiredFact]
    public async Task M006_LeavesOtherUsersAlone_WhenThereIsNoEvidenceTheirRowsWereClean()
    {
        await MigrateAsync("20261009141125_M004_AddNetWorthSnapshotCashSplit");
        await SeedAsync();
        await MigrateAsync(PreviousMigration);
        var afterM005 = await SnapshotsAsync();

        await MigrateAsync(RestoreMigration);

        var restored = await SnapshotsAsync();
        // First-ever row (no previous snapshot) and a series with no step anywhere: the M005 correction stays.
        ForUser(restored, _inflatedFromFirstRow).Should().Equal(ForUser(afterM005, _inflatedFromFirstRow));
        ForUser(restored, _neverInflated).Should().Equal(ForUser(afterM005, _neverInflated));
        ForUser(restored, _inflatedFromFirstRow).First().Brokerage.Should().Be(2.89m);
    }

    [DockerRequiredFact]
    public async Task RunningTheRestoreAgain_ChangesNothing()
    {
        await MigrateAsync("20261009141125_M004_AddNetWorthSnapshotCashSplit");
        await SeedAsync();
        await MigrateAsync(RestoreMigration);
        var snapshots = await SnapshotsAsync();
        var backups = await BackupCountAsync();

        await ExecuteAsync(M006_RestoreRowsBeforeInzhurInflation.RestoreSql);
        await ExecuteAsync(M006_RestoreRowsBeforeInzhurInflation.RestoreSql);

        (await SnapshotsAsync()).Should().Equal(snapshots);
        (await BackupCountAsync()).Should().Be(backups);
    }

    [DockerRequiredFact]
    public async Task Down_AppliesM005sRemovalAgain_ToExactlyTheRestoredRows_AndUpRestoresThemAgain()
    {
        await MigrateAsync("20261009141125_M004_AddNetWorthSnapshotCashSplit");
        await SeedAsync();
        await MigrateAsync(PreviousMigration);
        var afterM005 = await SnapshotsAsync();
        await MigrateAsync(RestoreMigration);
        var restored = await SnapshotsAsync();

        await MigrateAsync(PreviousMigration);
        (await SnapshotsAsync()).Should().Equal(afterM005);
        // Down twice over (the SQL again, no migration between) must not take the removal off a second time.
        await ExecuteAsync(M006_RestoreRowsBeforeInzhurInflation.ReapplyRemovalSql);
        (await SnapshotsAsync()).Should().Equal(afterM005);

        await MigrateAsync(RestoreMigration);
        (await SnapshotsAsync()).Should().Equal(restored);
    }

    [DockerRequiredFact]
    public async Task M006_LeavesARowChangedSinceM005_Alone()
    {
        await MigrateAsync("20261009141125_M004_AddNetWorthSnapshotCashSplit");
        await SeedAsync();
        await MigrateAsync(PreviousMigration);
        await ExecuteAsync($"""
            UPDATE net_worth_snapshots SET "BrokerageTotal" = 9999.00
            WHERE "UserId" = '{_owner}' AND "SnapshotDate" = DATE '2026-10-09'
            """);

        await MigrateAsync(RestoreMigration);

        var rows = await SnapshotsAsync();
        Row(rows, _owner, 9).Brokerage.Should().Be(9999.00m);
        Row(rows, _owner, 8).Brokerage.Should().Be(10912.40m);
    }
}
