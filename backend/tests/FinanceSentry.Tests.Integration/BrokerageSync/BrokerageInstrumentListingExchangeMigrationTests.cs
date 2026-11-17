namespace FinanceSentry.Tests.Integration.BrokerageSync;

using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

/// <summary>M017 adds the nullable ListingExchange column to BrokerageInstruments; rows that exist keep their data and read null.</summary>
[Trait("Category", "Integration")]
public sealed class BrokerageInstrumentListingExchangeMigrationTests : IAsyncLifetime
{
    private const string BeforeMigration = "20261016120000_M016_BrokerageFirstLinkedAt";
    private const string Migration = "20261117091340_M017_InstrumentListingExchange";

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

    private async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (T?)await cmd.ExecuteScalarAsync();
    }

    [DockerRequiredFact]
    public async Task Migrate_AddsANullableColumn_KeepsExistingRowsWithNull_AndDownDropsIt()
    {
        await using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeMigration);

        await using (var conn = new NpgsqlConnection(_database!.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO brokerage_sync."BrokerageInstruments"
                    ("Id","UserId","Provider","Conid","Symbol","InstrumentType","CreatedAt","UpdatedAt")
                VALUES (gen_random_uuid(), gen_random_uuid(), 'ibkr', 900000001, 'ZZZQ', 'STK', now(), now());
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync(Migration);

        (await ScalarAsync<string>(
            "SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'brokerage_sync' " +
            "AND table_name = 'BrokerageInstruments' AND column_name = 'ListingExchange'")).Should().Be("YES");
        (await ScalarAsync<long>("SELECT count(*) FROM brokerage_sync.\"BrokerageInstruments\" WHERE \"ListingExchange\" IS NULL"))
            .Should().Be(1, "an instrument stored before the migration has no exchange until a Flex statement reports one");

        await migrator.MigrateAsync(BeforeMigration);

        (await ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'brokerage_sync' " +
            "AND table_name = 'BrokerageInstruments' AND column_name = 'ListingExchange'")).Should().Be(0);
        (await ScalarAsync<long>("SELECT count(*) FROM brokerage_sync.\"BrokerageInstruments\"")).Should().Be(1);
    }
}
