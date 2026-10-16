namespace FinanceSentry.Tests.Integration.BrokerageSync;

using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

/// <summary>M016 adds FirstLinkedAt to the IBKR credential tables and backfills it from each row's CreatedAt.</summary>
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
    public async Task Migrate_BackfillsFirstLinkedAtFromCreatedAt_AndDownDropsIt()
    {
        var created = new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc);
        await using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeMigration);

        await using (var conn = new NpgsqlConnection(_database!.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO brokerage_sync."IBKRFlexCredentials"
                    ("Id","UserId","IsActive","CreatedAt","QueryId","EncryptedToken","TokenIv","TokenAuthTag","KeyVersion")
                VALUES (gen_random_uuid(), gen_random_uuid(), true, @created, 'q', '\x01', '\x02', '\x03', 1)
                """;
            cmd.Parameters.AddWithValue("created", created);
            await cmd.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync(Migration);

        await using (var conn = new NpgsqlConnection(_database.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT \"FirstLinkedAt\" FROM brokerage_sync.\"IBKRFlexCredentials\"";
            ((DateTime)(await cmd.ExecuteScalarAsync())!).Should().Be(created);
        }

        await migrator.MigrateAsync(BeforeMigration);
        await using var conn2 = new NpgsqlConnection(_database.ConnectionString);
        await conn2.OpenAsync();
        await using var check = conn2.CreateCommand();
        check.CommandText = "SELECT count(*) FROM information_schema.columns WHERE column_name = 'FirstLinkedAt' AND table_schema = 'brokerage_sync'";
        ((long)(await check.ExecuteScalarAsync())!).Should().Be(0);
    }
}
