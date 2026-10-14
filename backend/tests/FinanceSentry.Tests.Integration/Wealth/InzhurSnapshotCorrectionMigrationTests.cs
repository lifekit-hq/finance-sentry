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
/// M005_CorrectInzhurNetWorthHistory against real Postgres, on production-shaped history: the Inzhur connection
/// made on 2026-10-12, an inflated sync that day, the fixed build's first sync (holdings in hryvnia plus the full
/// ledger) on 2026-10-13 07:00Z, and an IBKR account alongside. The fixture rate is 40 UAH/USD, so every expected
/// figure below is exact:
/// <list type="bullet">
/// <item>Corrected holdings: MilTech 4 units, 4,134.80 UAH ($103.37); REIT 92 units, 1,062.60 UAH ($26.565);
/// cash 21.19 UAH ($0.52975).</item>
/// <item>Ledger after the 10-12 18:00Z snapshot: a 9.10 UAH distribution (20:00Z), a 20.00 UAH deposit (20:30Z) and
/// one REIT unit bought for 11.55 UAH (21:00Z). At 18:00Z Inzhur therefore held 4 MilTech + 91 REIT
/// ($129.64625) and 3.64 UAH of cash ($0.091); at 10-13 01:00Z it held today's position ($129.935, $0.52975).</item>
/// <item>The inflated rows stored 100 times that, so 99 times it comes off.</item>
/// </list>
/// </summary>
[Trait("Category", "Integration")]
public sealed class InzhurSnapshotCorrectionMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261009141125_M004_AddNetWorthSnapshotCashSplit";
    private const string CorrectionMigration = "20261014120000_M005_CorrectInzhurNetWorthHistory";

    private static readonly DateTimeOffset FixedSyncAt = new(2026, 10, 13, 7, 0, 5, TimeSpan.Zero);

    private readonly Guid _inzhurUser = Guid.NewGuid();
    private readonly Guid _ibkrOnlyUser = Guid.NewGuid();
    private readonly Guid _lateInzhurUser = Guid.NewGuid();
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
        Guid UserId, DateOnly Date, decimal Banking, decimal Brokerage, decimal Crypto, decimal Total,
        decimal? Cash, decimal? BrokerageInvested, decimal? CryptoInvested);

    private sealed record BackupRow(
        Guid UserId, DateOnly Date, decimal Brokerage, decimal Total, decimal? Cash, decimal? BrokerageInvested,
        decimal InzhurInvested, decimal InzhurCash, decimal RemovedInvested, decimal RemovedCash);

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
                (gen_random_uuid(), '{{_inzhurUser}}', 'Active', TIMESTAMPTZ '2026-10-12 10:30:00Z', TIMESTAMPTZ '2026-10-14 07:00:04Z', '\x00', '\x00', '\x00', 'Healthy', 0),
                (gen_random_uuid(), '{{_lateInzhurUser}}', 'Active', TIMESTAMPTZ '2026-10-13 12:00:00Z', TIMESTAMPTZ '2026-10-14 07:00:04Z', '\x00', '\x00', '\x00', 'Healthy', 0);

            -- Holdings as the fixed build writes them (hryvnia native values, USD at 40 UAH/USD), next to IBKR.
            INSERT INTO brokerage_sync."BrokerageHoldings"
                ("Id", "UserId", "Symbol", "InstrumentType", "Quantity", "UsdValue", "SyncedAt", "Provider", "Currency", "NativeValue")
            VALUES
                (gen_random_uuid(), '{{_inzhurUser}}', 'Inzhur MilTech', 'FUND', 4, 103.37, TIMESTAMPTZ '2026-10-14 07:00:04Z', 'inzhur', 'UAH', 4134.80),
                (gen_random_uuid(), '{{_inzhurUser}}', 'Inzhur REIT', 'FUND', 92, 26.565, TIMESTAMPTZ '2026-10-14 07:00:04Z', 'inzhur', 'UAH', 1062.60),
                (gen_random_uuid(), '{{_inzhurUser}}', 'UAH Cash', 'CASH', 21.19, 0.52975, TIMESTAMPTZ '2026-10-14 07:00:04Z', 'inzhur', 'UAH', 21.19),
                (gen_random_uuid(), '{{_inzhurUser}}', 'VWRA', 'STK', 120, 17000, TIMESTAMPTZ '2026-10-14 06:00:00Z', 'ibkr', 'USD', NULL),
                (gen_random_uuid(), '{{_ibkrOnlyUser}}', 'VWRA', 'STK', 50, 7000, TIMESTAMPTZ '2026-10-14 06:00:00Z', 'ibkr', 'USD', NULL),
                (gen_random_uuid(), '{{_lateInzhurUser}}', 'Inzhur REIT', 'FUND', 10, 2.8875, TIMESTAMPTZ '2026-10-14 07:00:04Z', 'inzhur', 'UAH', 115.50);

            -- The ledger, imported by the fixed build's first sync (CreatedAt), dated by Inzhur (TradeDateTime / DateTime).
            INSERT INTO brokerage_sync."BrokerageTrades"
                ("Id", "UserId", "Provider", "IbExecutionId", "IbTradeId", "Symbol", "TradeDateTime", "Quantity", "Price",
                 "Proceeds", "Currency", "CreatedAt", "UpdatedAt")
            VALUES
                (gen_random_uuid(), '{{_inzhurUser}}', 'inzhur', 'tx-1', 'tx-1', 'Inzhur MilTech', TIMESTAMPTZ '2025-11-03 09:00:00Z', 4, 1000, -4000, 'UAH', TIMESTAMPTZ '{{FixedSyncAt:O}}', TIMESTAMPTZ '{{FixedSyncAt:O}}'),
                (gen_random_uuid(), '{{_inzhurUser}}', 'inzhur', 'tx-2', 'tx-2', 'Inzhur REIT', TIMESTAMPTZ '2025-12-01 09:00:00Z', 91, 11, -1001, 'UAH', TIMESTAMPTZ '{{FixedSyncAt:O}}', TIMESTAMPTZ '{{FixedSyncAt:O}}'),
                (gen_random_uuid(), '{{_inzhurUser}}', 'inzhur', 'tx-5', 'tx-5', 'Inzhur REIT', TIMESTAMPTZ '2026-10-12 21:00:00Z', 1, 11.55, -11.55, 'UAH', TIMESTAMPTZ '{{FixedSyncAt:O}}', TIMESTAMPTZ '{{FixedSyncAt:O}}'),
                (gen_random_uuid(), '{{_ibkrOnlyUser}}', 'ibkr', 'ib-1', 'ib-1', 'VWRA', TIMESTAMPTZ '2026-10-12 15:00:00Z', 1, 140, -140, 'USD', TIMESTAMPTZ '2026-10-10 06:00:00Z', TIMESTAMPTZ '2026-10-10 06:00:00Z'),
                (gen_random_uuid(), '{{_lateInzhurUser}}', 'inzhur', 'tx-9', 'tx-9', 'Inzhur REIT', TIMESTAMPTZ '2026-09-01 09:00:00Z', 10, 11, -110, 'UAH', TIMESTAMPTZ '2026-10-13 12:00:30Z', TIMESTAMPTZ '2026-10-13 12:00:30Z');

            INSERT INTO brokerage_sync."BrokerageCashTransactions"
                ("Id", "UserId", "Provider", "IdempotencyKey", "TransactionType", "Amount", "Currency", "DateTime", "CreatedAt")
            VALUES
                (gen_random_uuid(), '{{_inzhurUser}}', 'inzhur', 'tx-0', 'Deposit', 5004.64, 'UAH', TIMESTAMPTZ '2025-11-01 09:00:00Z', TIMESTAMPTZ '{{FixedSyncAt:O}}'),
                (gen_random_uuid(), '{{_inzhurUser}}', 'inzhur', 'tx-3', 'Distribution crediting', 9.10, 'UAH', TIMESTAMPTZ '2026-10-12 20:00:00Z', TIMESTAMPTZ '{{FixedSyncAt:O}}'),
                (gen_random_uuid(), '{{_inzhurUser}}', 'inzhur', 'tx-4', 'Deposit', 20.00, 'UAH', TIMESTAMPTZ '2026-10-12 20:30:00Z', TIMESTAMPTZ '{{FixedSyncAt:O}}');

            -- Net-worth history. 10-12 and 10-13 were taken from the inflated Inzhur holdings; 10-11 predates the
            -- connection and 10-14 follows the fixed sync. The other users' rows sit inside the same dates.
            INSERT INTO net_worth_snapshots
                ("Id", "UserId", "SnapshotDate", "BankingTotal", "BrokerageTotal", "CryptoTotal", "TotalNetWorth", "Currency",
                 "TakenAt", cash_total, brokerage_invested, crypto_invested)
            VALUES
                (gen_random_uuid(), '{{_inzhurUser}}', DATE '2026-10-11', 5000, 17600.00, 1000, 23600.00, 'USD', TIMESTAMPTZ '2026-10-11 01:00:00Z', 5600.00, 17000.00, 1000),
                (gen_random_uuid(), '{{_inzhurUser}}', DATE '2026-10-12', 5000, 30573.73, 1000, 36573.73, 'USD', TIMESTAMPTZ '2026-10-12 18:00:00Z', 5609.10, 29964.63, 1000),
                (gen_random_uuid(), '{{_inzhurUser}}', DATE '2026-10-13', 5000, 30646.48, 1000, 36646.48, 'USD', TIMESTAMPTZ '2026-10-13 01:00:00Z', NULL, NULL, NULL),
                (gen_random_uuid(), '{{_inzhurUser}}', DATE '2026-10-14', 5000, 17730.46, 1000, 23730.46, 'USD', TIMESTAMPTZ '2026-10-14 01:00:00Z', 5600.53, 17129.93, 1000),
                (gen_random_uuid(), '{{_ibkrOnlyUser}}', DATE '2026-10-12', 2000, 7000.00, 0, 9000.00, 'USD', TIMESTAMPTZ '2026-10-12 18:00:00Z', 2000.00, 7000.00, 0),
                (gen_random_uuid(), '{{_lateInzhurUser}}', DATE '2026-10-13', 300, 2.89, 0, 302.89, 'USD', TIMESTAMPTZ '2026-10-13 10:00:00Z', NULL, NULL, NULL);
            """);
    }

    private async Task<List<SnapshotRow>> SnapshotsAsync()
    {
        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT "UserId", "SnapshotDate", "BankingTotal", "BrokerageTotal", "CryptoTotal", "TotalNetWorth",
                   cash_total, brokerage_invested, crypto_invested
            FROM net_worth_snapshots ORDER BY "UserId", "SnapshotDate"
            """, conn);
        var rows = new List<SnapshotRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new SnapshotRow(reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1), reader.GetDecimal(2),
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5), NullableDecimal(reader, 6),
                NullableDecimal(reader, 7), NullableDecimal(reader, 8)));
        }
        return rows;
    }

    private async Task<List<BackupRow>> BackupAsync()
    {
        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"""
            SELECT "UserId", "SnapshotDate", "BrokerageTotal", "TotalNetWorth", cash_total, brokerage_invested,
                   inzhur_invested_usd, inzhur_cash_usd, removed_invested_usd, removed_cash_usd
            FROM {M005_CorrectInzhurNetWorthHistory.BackupTable} ORDER BY "SnapshotDate"
            """, conn);
        var rows = new List<BackupRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new BackupRow(reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1), reader.GetDecimal(2),
                reader.GetDecimal(3), NullableDecimal(reader, 4), NullableDecimal(reader, 5), reader.GetDecimal(6),
                reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9)));
        }
        return rows;
    }

    private static decimal? NullableDecimal(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private static SnapshotRow Row(IEnumerable<SnapshotRow> rows, Guid userId, int day) =>
        rows.Single(r => r.UserId == userId && r.Date == new DateOnly(2026, 10, day));

    [DockerRequiredFact]
    public async Task Up_RemovesTheInflatedInzhurValue_FromTheRowsTakenBeforeTheFixedSync()
    {
        await MigrateAsync(PreviousMigration);
        await SeedAsync();

        await MigrateAsync(CorrectionMigration);

        var rows = await SnapshotsAsync();
        // 10-12 18:00Z: IBKR 17,600 + Inzhur $129.65 invested + $0.09 cash; the split moves with it.
        Row(rows, _inzhurUser, 12).Should().Be(
            new SnapshotRow(_inzhurUser, new DateOnly(2026, 10, 12), 5000, 17729.74m, 1000, 23729.74m, 5600.09m, 17129.65m, 1000));
        // 10-13 01:00Z: today's position, before the 07:00Z fixed sync; a null split stays null.
        Row(rows, _inzhurUser, 13).Should().Be(
            new SnapshotRow(_inzhurUser, new DateOnly(2026, 10, 13), 5000, 17730.46m, 1000, 23730.46m, null, null, null));
    }

    [DockerRequiredFact]
    public async Task Up_BacksUpEveryRowItChanges_WithItsOriginalValuesAndTheDerivation()
    {
        await MigrateAsync(PreviousMigration);
        await SeedAsync();

        await MigrateAsync(CorrectionMigration);

        (await BackupAsync()).Should().Equal(
            new BackupRow(_inzhurUser, new DateOnly(2026, 10, 12), 30573.73m, 36573.73m, 5609.10m, 29964.63m,
                129.6463m, 0.0910m, 12834.98m, 9.01m),
            new BackupRow(_inzhurUser, new DateOnly(2026, 10, 13), 30646.48m, 36646.48m, null, null,
                129.9350m, 0.5298m, 12863.57m, 52.45m));
    }

    [DockerRequiredFact]
    public async Task Up_LeavesOtherUsersAndRowsOutsideTheInflatedWindowUntouched()
    {
        await MigrateAsync(PreviousMigration);
        await SeedAsync();
        var before = await SnapshotsAsync();

        await MigrateAsync(CorrectionMigration);

        var after = await SnapshotsAsync();
        Row(after, _inzhurUser, 11).Should().Be(Row(before, _inzhurUser, 11));
        Row(after, _inzhurUser, 14).Should().Be(Row(before, _inzhurUser, 14));
        Row(after, _ibkrOnlyUser, 12).Should().Be(Row(before, _ibkrOnlyUser, 12));
        Row(after, _lateInzhurUser, 13).Should().Be(Row(before, _lateInzhurUser, 13));
    }

    [DockerRequiredFact]
    public async Task RunningTheCorrectionAgain_ChangesNothing()
    {
        await MigrateAsync(PreviousMigration);
        await SeedAsync();
        await MigrateAsync(CorrectionMigration);
        var snapshots = await SnapshotsAsync();
        var backup = await BackupAsync();

        await ExecuteAsync(M005_CorrectInzhurNetWorthHistory.CorrectionSql);
        await ExecuteAsync(M005_CorrectInzhurNetWorthHistory.CorrectionSql);

        (await SnapshotsAsync()).Should().Equal(snapshots);
        (await BackupAsync()).Should().Equal(backup);
    }

    [DockerRequiredFact]
    public async Task Down_RestoresTheOriginals_AndUpAppliesTheSameCorrectionAgain()
    {
        await MigrateAsync(PreviousMigration);
        await SeedAsync();
        var original = await SnapshotsAsync();
        await MigrateAsync(CorrectionMigration);
        var corrected = await SnapshotsAsync();

        await MigrateAsync(PreviousMigration);
        (await SnapshotsAsync()).Should().Equal(original);

        await MigrateAsync(CorrectionMigration);
        (await SnapshotsAsync()).Should().Equal(corrected);
        (await BackupAsync()).Should().HaveCount(2);
    }

    [DockerRequiredFact]
    public async Task Up_OnADatabaseWithoutTheBrokerageSchema_CreatesTheEmptyBackupAndChangesNothing()
    {
        await using (var wealth = WealthContext())
            await wealth.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await ExecuteAsync($"""
            INSERT INTO net_worth_snapshots ("Id", "UserId", "SnapshotDate", "BankingTotal", "BrokerageTotal", "CryptoTotal", "TotalNetWorth", "Currency", "TakenAt")
            VALUES (gen_random_uuid(), '{_inzhurUser}', DATE '2026-10-12', 5000, 30573.73, 1000, 36573.73, 'USD', TIMESTAMPTZ '2026-10-12 18:00:00Z')
            """);

        await using (var wealth = WealthContext())
            await wealth.GetService<IMigrator>().MigrateAsync(CorrectionMigration);

        (await SnapshotsAsync()).Single().Total.Should().Be(36573.73m);
        (await BackupAsync()).Should().BeEmpty();
    }
}
