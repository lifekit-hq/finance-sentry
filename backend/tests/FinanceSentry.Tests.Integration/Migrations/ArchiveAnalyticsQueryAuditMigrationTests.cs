namespace FinanceSentry.Tests.Integration.Migrations;

using FinanceSentry.Modules.Retention.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Auth;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

/// <summary>
/// Retention M002 retires the removed Analytics module's objects: <c>analytics.query_audit</c> moves, rows and
/// all, to <c>retention.analytics_query_audit_archive</c>; the views, the <c>fs_readonly</c> role and the schema
/// go. Down puts all of it back. Runs against a database migrated by the API's own startup path.
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgresClusterStateCollection.Name)]
public sealed class ArchiveAnalyticsQueryAuditMigrationTests : IAsyncLifetime
{
    private const string Before = "20260806210425_M001_InitialSchema";
    private const string Migration = "20261122120000_M002_ArchiveAnalyticsQueryAudit";
    private static readonly string[] Views = ["v_transactions", "v_holdings", "v_analyst_actions", "v_net_worth_daily", "v_budgets"];

    private TestDatabase? _database;

    public async Task InitializeAsync() =>
        _database = await PostgresServer.Postgres14.CreateDatabaseAsync(resetsClusterRoles: true);

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    [DockerRequiredFact]
    public async Task Up_ArchivesTheAuditRows_AndDown_RestoresTheTableViewsAndRole()
    {
        await using var factory = new PostgresApiFactory(_database!.ConnectionString);
        using var client = factory.CreateClient(); // startup migrates every module, Retention M002 included
        using var scope = factory.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<RetentionDbContext>().GetService<IMigrator>();

        await using var conn = new NpgsqlConnection(_database.ConnectionString);
        await conn.OpenAsync();

        // Fresh database: the Analytics objects never existed, and nothing is left behind.
        (await ObjectsPresentAsync(conn)).Should().Be(new State(false, false, 0, false));

        // Down restores the table, the five views and the role; an audit row written now is the one to archive.
        await migrator.MigrateAsync(Before);
        (await ObjectsPresentAsync(conn)).Should().Be(new State(true, false, 5, true));
        var id = Guid.NewGuid();
        await ExecuteAsync(conn,
            """INSERT INTO analytics.query_audit ("Id","UserId","Sql","Outcome","CreatedAt") VALUES (@id, gen_random_uuid(), 'select 1', 'ok', now())""",
            ("id", id));

        // Up again: the row moves to the archive; views, role and schema are gone.
        await migrator.MigrateAsync(Migration);
        (await ObjectsPresentAsync(conn)).Should().Be(new State(false, true, 0, false));
        (await ScalarAsync(conn, "SELECT count(*) FROM retention.analytics_query_audit_archive WHERE \"Id\" = @id", ("id", id)))
            .Should().Be(1L);

        // And Down once more brings the very same row back.
        await migrator.MigrateAsync(Before);
        (await ObjectsPresentAsync(conn)).Should().Be(new State(true, false, 5, true));
        (await ScalarAsync(conn, "SELECT count(*) FROM analytics.query_audit WHERE \"Id\" = @id", ("id", id)))
            .Should().Be(1L);

        await migrator.MigrateAsync(Migration);
    }

    private sealed record State(bool LiveTable, bool ArchiveTable, long Views, bool Role);

    private static async Task<State> ObjectsPresentAsync(NpgsqlConnection conn) => new(
        (long)(await ScalarAsync(conn, "SELECT count(*) FROM pg_tables WHERE schemaname = 'analytics' AND tablename = 'query_audit'"))! == 1,
        (long)(await ScalarAsync(conn, "SELECT count(*) FROM pg_tables WHERE schemaname = 'retention' AND tablename = 'analytics_query_audit_archive'"))! == 1,
        (long)(await ScalarAsync(conn, "SELECT count(*) FROM pg_views WHERE schemaname = 'analytics' AND viewname = ANY(@views)", ("views", Views)))!,
        (long)(await ScalarAsync(conn, "SELECT count(*) FROM pg_roles WHERE rolname = 'fs_readonly'"))! == 1);

    private static async Task<object?> ScalarAsync(NpgsqlConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return await command.ExecuteScalarAsync();
    }

    private static Task ExecuteAsync(NpgsqlConnection conn, string sql, params (string Name, object Value)[] parameters) =>
        ScalarAsync(conn, sql, parameters);
}
