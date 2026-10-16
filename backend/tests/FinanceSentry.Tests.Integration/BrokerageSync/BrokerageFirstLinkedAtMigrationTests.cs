namespace FinanceSentry.Tests.Integration.BrokerageSync;

using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

/// <summary>M016 adds FirstLinkedAt to the IBKR credential tables and backfills it from the earliest evidence a relink cannot have moved.</summary>
[Trait("Category", "Integration")]
public sealed class BrokerageFirstLinkedAtMigrationTests : IAsyncLifetime
{
    private const string BeforeMigration = "20261015120000_M015_ConnectionHealthNotBefore";
    private const string Migration = "20261016120000_M016_BrokerageFirstLinkedAt";

    private TestDatabase? _database;

    public async Task InitializeAsync() => _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private BrokerageSyncDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<BrokerageSyncDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    [DockerRequiredFact]
    public async Task Migrate_BackfillsFromEarliestEvidence_NotFromARelinkRestampedCreatedAt_AndDownDropsIt()
    {
        var relinked = new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc);
        var firstSynced = new DateTime(2026, 7, 10, 6, 0, 0, DateTimeKind.Utc);
        var userId = Guid.NewGuid();
        var untouchedUser = Guid.NewGuid();
        await using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeMigration);

        await using (var conn = new NpgsqlConnection(_database!.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            // A Flex credential relinked on 2026-09-01 (CreatedAt re-stamped), whose first IBKR cash row was stored 2026-07-10;
            // a second user whose credential was never relinked and has no synced rows.
            cmd.CommandText = """
                INSERT INTO brokerage_sync."IBKRFlexCredentials"
                    ("Id","UserId","IsActive","CreatedAt","QueryId","EncryptedToken","TokenIv","TokenAuthTag","KeyVersion")
                VALUES (gen_random_uuid(), @user, true, @relinked, 'q', '\x01', '\x02', '\x03', 1),
                       (gen_random_uuid(), @untouched, true, @relinked, 'q', '\x01', '\x02', '\x03', 1);
                INSERT INTO brokerage_sync."BrokerageCashTransactions"
                    ("Id","UserId","Provider","IdempotencyKey","DateTime","Amount","TransactionType","Currency","CreatedAt")
                VALUES (gen_random_uuid(), @user, 'ibkr', 'k1', @firstSynced, 100, 'Deposits/Withdrawals', 'USD', @firstSynced);
                """;
            cmd.Parameters.AddWithValue("user", userId);
            cmd.Parameters.AddWithValue("untouched", untouchedUser);
            cmd.Parameters.AddWithValue("relinked", relinked);
            cmd.Parameters.AddWithValue("firstSynced", firstSynced);
            await cmd.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync(Migration);

        await using (var conn = new NpgsqlConnection(_database.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT \"FirstLinkedAt\" FROM brokerage_sync.\"IBKRFlexCredentials\" WHERE \"UserId\" = @u";
            cmd.Parameters.AddWithValue("u", userId);
            ((DateTime)(await cmd.ExecuteScalarAsync())!).Should().Be(firstSynced, "the stored cash row predates the relink");
            cmd.Parameters["u"].Value = untouchedUser;
            ((DateTime)(await cmd.ExecuteScalarAsync())!).Should().Be(relinked, "no earlier evidence exists, so CreatedAt stands");
        }

        await migrator.MigrateAsync(BeforeMigration);
        await using var conn2 = new NpgsqlConnection(_database.ConnectionString);
        await conn2.OpenAsync();
        await using var check = conn2.CreateCommand();
        check.CommandText = "SELECT count(*) FROM information_schema.columns WHERE column_name = 'FirstLinkedAt' AND table_schema = 'brokerage_sync'";
        ((long)(await check.ExecuteScalarAsync())!).Should().Be(0);
    }
}
