namespace FinanceSentry.Tests.Integration.Shared;

using System.Collections.Concurrent;
using FluentAssertions;
using Npgsql;
using Xunit;

/// <summary>
/// The reset between tests on a shared <see cref="PostgresServer"/> is complete. Every test here first asserts
/// it sees nothing an earlier test left behind - tables and rows, the earlier test's database itself, a
/// cluster-wide role, a session still attached - and then leaves all of that behind for the next one. Each
/// test is both the polluter and the probe, so whichever order xUnit picks, every test after the first fails
/// if the reset leaks; all of them also assert they ran on the same server, so the check cannot pass by
/// accident on a fresh container.
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgresClusterStateCollection.Name)]
public sealed class SharedPostgresIsolationTests : IAsyncLifetime
{
    private const string ProbeRole = "isolation_probe";

    private static readonly ConcurrentQueue<string> FinishedDatabases = new();
    private static readonly ConcurrentQueue<DateTime> ServerStartTimes = new();

    private TestDatabase? _database;
    private NpgsqlConnection? _leftOpen;

    public async Task InitializeAsync() =>
        _database = await PostgresServer.Postgres14.CreateDatabaseAsync(resetsClusterRoles: true);

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            // Dropped with a session still attached: the reset must not depend on every test closing its connections.
            await _database.DisposeAsync();
            FinishedDatabases.Enqueue(_database.Name);
        }

        if (_leftOpen is not null)
            await _leftOpen.DisposeAsync();
    }

    [DockerRequiredFact]
    public Task A_test_sees_none_of_the_state_an_earlier_test_left() => AssertPristineThenLeaveStateBehindAsync();

    [DockerRequiredFact]
    public Task Another_test_sees_none_of_the_state_an_earlier_test_left() => AssertPristineThenLeaveStateBehindAsync();

    [DockerRequiredFact]
    public Task A_third_test_sees_none_of_the_state_an_earlier_test_left() => AssertPristineThenLeaveStateBehindAsync();

    private async Task AssertPristineThenLeaveStateBehindAsync()
    {
        _leftOpen = new NpgsqlConnection(_database!.ConnectionString);
        await _leftOpen.OpenAsync();

        var startedAt = (DateTime)(await ScalarAsync("SELECT pg_postmaster_start_time()"))!;
        foreach (var earlier in ServerStartTimes)
        {
            earlier.Should().Be(startedAt,
                "every test must run on the one shared server, or this check proves nothing about the reset");
        }

        ServerStartTimes.Enqueue(startedAt);

        (await ScalarAsync(
                "SELECT count(*) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema')"))
            .Should().Be(0L, "a test's database starts empty");
        (await ScalarAsync("SELECT count(*) FROM pg_database WHERE datname = ANY(@finished)",
                ("finished", FinishedDatabases.ToArray())))
            .Should().Be(0L, "an earlier test's database is dropped when that test ends");
        (await ScalarAsync("SELECT count(*) FROM pg_roles WHERE rolname = @role", ("role", ProbeRole)))
            .Should().Be(0L, "a role an earlier test created is dropped when that test ends");

        // The shape Analytics' migration leaves: a table, rows, and a role holding privileges on it.
        await ScalarAsync("CREATE TABLE leak_probe (id int PRIMARY KEY)");
        await ScalarAsync("INSERT INTO leak_probe VALUES (1)");
        await ScalarAsync($"CREATE ROLE {ProbeRole} NOLOGIN");
        await ScalarAsync($"GRANT SELECT ON leak_probe TO {ProbeRole}");
    }

    private async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, _leftOpen);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return await command.ExecuteScalarAsync();
    }
}
