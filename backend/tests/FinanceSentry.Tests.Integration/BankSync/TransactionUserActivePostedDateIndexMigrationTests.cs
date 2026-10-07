namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

/// <summary>
/// #418 S1: the composed transaction filter query relies on
/// <c>idx_transaction_user_active_posted_date</c> for the <c>(UserId, IsActive, PostedDate)</c> scan;
/// this confirms the CLI-generated migration actually creates it on a real Postgres, not just that
/// the model snapshot says it should.
///
/// Requires Docker (<see cref="DockerRequiredFactAttribute"/> skips otherwise; CI has it).
/// </summary>
[Trait("Category", "Integration")]
public sealed class TransactionUserActivePostedDateIndexMigrationTests : IAsyncLifetime
{
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    [DockerRequiredFact]
    public async Task Migrate_CreatesTheUserActivePostedDateIndex_OnTheTransactionsTable()
    {
        await using (var ctx = new BankSyncDbContext(new DbContextOptionsBuilder<BankSyncDbContext>()
            .UseNpgsql(_database!.ConnectionString)
            .Options, NoCurrentUser.Instance))
        {
            await ctx.Database.MigrateAsync();
        }

        await using var conn = new NpgsqlConnection(_database!.ConnectionString);
        await conn.OpenAsync();
        await using var indexColumns = new NpgsqlCommand(
            """
            SELECT string_agg(a.attname, ',' ORDER BY array_position(ix.indkey, a.attnum))
            FROM pg_index ix
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_class t ON t.oid = ix.indrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey)
            WHERE n.nspname = 'bank_sync' AND t.relname = 'Transactions' AND i.relname = 'idx_transaction_user_active_posted_date'
            GROUP BY i.relname
            """,
            conn);
        var columns = (string?)await indexColumns.ExecuteScalarAsync();

        columns.Should().Be("UserId,IsActive,PostedDate");
    }
}
