namespace FinanceSentry.Tests.Integration.CryptoSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

/// <summary>M011 adds FirstLinkedAt and FlowsWalkLane to the exchange credentials and backfills FirstLinkedAt from CreatedAt.</summary>
[Trait("Category", "Integration")]
public sealed class CryptoFirstLinkedAtMigrationTests : IAsyncLifetime
{
    private const string BeforeMigration = "20261016092559_M010_CryptoFlowEvents";
    private const string Migration = "20261016120100_M011_CryptoFirstLinkedAt";

    private TestDatabase? _database;

    public async Task InitializeAsync() => _database = await PostgresServer.Postgres14.CreateDatabaseAsync();

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private CryptoSyncDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CryptoSyncDbContext>()
            .UseNpgsql(_database!.ConnectionString, b => b.MigrationsHistoryTable("__EFMigrationsHistory", "public"))
            .Options, NoCurrentUser.Instance);

    [DockerRequiredFact]
    public async Task Migrate_BackfillsFirstLinkedAtFromCreatedAt_StartsEveryWalkOnLaneZero_AndDownDropsBoth()
    {
        var created = new DateTime(2026, 8, 3, 7, 15, 0, DateTimeKind.Utc);
        var walkedTo = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var credentialId = Guid.NewGuid();
        await using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeMigration);

        await using (var conn = new NpgsqlConnection(_database!.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO crypto_sync."ExchangeCredentials"
                    ("Id","UserId","Provider","EncryptedApiKey","ApiKeyIv","ApiKeyAuthTag",
                     "EncryptedApiSecret","ApiSecretIv","ApiSecretAuthTag","KeyVersion","IsActive","CreatedAt","FlowsWalkedTo")
                VALUES (@id, @user, 'revolut-x', '\x01', '\x02', '\x03', '\x01', '\x02', '\x03', 1, true, @created, @walked);
                """;
            cmd.Parameters.AddWithValue("id", credentialId);
            cmd.Parameters.AddWithValue("user", Guid.NewGuid());
            cmd.Parameters.AddWithValue("created", created);
            cmd.Parameters.AddWithValue("walked", walkedTo);
            await cmd.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync(Migration);

        await using (var conn = new NpgsqlConnection(_database.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT "FirstLinkedAt", "CreatedAt", "FlowsWalkLane", "FlowsWalkedTo"
                FROM crypto_sync."ExchangeCredentials" WHERE "Id" = @id
                """;
            cmd.Parameters.AddWithValue("id", credentialId);
            await using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetDateTime(0).Should().Be(created);
            reader.GetDateTime(1).Should().Be(created, "the backfill touches no existing column");
            reader.GetInt32(2).Should().Be(0);
            reader.GetDateTime(3).Should().Be(walkedTo);
        }

        await migrator.MigrateAsync(BeforeMigration);
        await using var conn2 = new NpgsqlConnection(_database.ConnectionString);
        await conn2.OpenAsync();
        await using var check = conn2.CreateCommand();
        check.CommandText = """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = 'crypto_sync' AND table_name = 'ExchangeCredentials' AND column_name IN ('FirstLinkedAt','FlowsWalkLane')
            """;
        ((long)(await check.ExecuteScalarAsync())!).Should().Be(0);
    }
}
