namespace FinanceSentry.Tests.Integration.Shared;

using Npgsql;
using Testcontainers.PostgreSql;

/// <summary>
/// One Postgres container per image for the whole test run, started on first use, in which every test gets a
/// database of its own (<see cref="TestDatabase"/>). Starting a container per test method spent ~80% of each
/// container's life on start-up; a database inside a running server costs a <c>CREATE DATABASE</c>.
/// <para>
/// A static singleton rather than an xUnit fixture: xUnit 2 has no assembly fixture, and a collection fixture
/// would put every container-backed class into one collection and run them serially. The containers are
/// disposed (stopped, removed, with their anonymous volume) when the last test has finished, by
/// <see cref="ContainerTeardownTestFramework"/>; the Testcontainers resource reaper (Ryuk) stays on as the
/// backstop for a run that is killed before then. Ryuk alone is not enough: wherever it is disabled
/// (<c>TESTCONTAINERS_RYUK_DISABLED=true</c>) nothing else removes the container.
/// </para>
/// <para>
/// Isolation is by database, which Postgres enforces: schemas, tables, rows, sequences, function grants, EF
/// migration history and Hangfire storage all live inside one database and are dropped with it. Roles are the
/// one cluster-wide object the app's migrations create (Analytics' <c>fs_readonly</c>); the classes that run
/// those migrations share <see cref="PostgresClusterStateCollection"/>, and their databases also drop every
/// role created since the server started.
/// </para>
/// </summary>
public sealed class PostgresServer
{
    // Each test brings its own connection pool; the default 100 would cap the parallel run.
    private const string MaxConnections = "max_connections=500";

    private readonly string _image;
    private Lazy<Task<Started>> _started;

    private PostgresServer(string image)
    {
        _image = image;
        _started = NewLazyStart();
    }

    /// <summary>The production major version.</summary>
    public static PostgresServer Postgres14 { get; } = new("postgres:14-alpine");

    public static PostgresServer Postgres16 { get; } = new("postgres:16-alpine");

    /// <summary>A new, empty database, dropped when the returned handle is disposed.</summary>
    /// <param name="resetsClusterRoles">
    /// Also drop, on dispose, every role created since the server started. Only for classes in
    /// <see cref="PostgresClusterStateCollection"/>: anywhere else it could drop a role a parallel test is using.
    /// </param>
    public async Task<TestDatabase> CreateDatabaseAsync(bool resetsClusterRoles = false)
    {
        var database = await ReserveDatabaseAsync(resetsClusterRoles);
        await database.CreateAsync();
        return database;
    }

    /// <summary>
    /// A database name on this server that does not exist yet, for code that must create it itself; dropped,
    /// if it was created, when the returned handle is disposed.
    /// </summary>
    public async Task<TestDatabase> ReserveDatabaseAsync(bool resetsClusterRoles = false)
    {
        var started = await _started.Value;
        return new TestDatabase(started.AdminConnectionString, $"test_{Guid.NewGuid():N}",
            resetsClusterRoles ? started.BaselineRoles : null);
    }

    /// <summary>
    /// Disposes every container this process started, which also removes it and its anonymous volume. A server
    /// that never started is left alone; one disposed here starts afresh if it is used again.
    /// </summary>
    internal static async Task DisposeAllAsync()
    {
        await Task.WhenAll(Postgres14.DisposeContainerAsync(), Postgres16.DisposeContainerAsync());
    }

    private Lazy<Task<Started>> NewLazyStart() => new(() => StartAsync(_image));

    private async Task DisposeContainerAsync()
    {
        var started = _started;
        if (!started.IsValueCreated)
            return;

        _started = NewLazyStart();
        if (started.Value.IsFaulted)
            return; // The start itself failed, so there is no container of ours to dispose.

        await (await started.Value).Container.DisposeAsync();
    }

    private static async Task<Started> StartAsync(string image)
    {
        var container = new PostgreSqlBuilder(image).WithCommand("-c", MaxConnections).Build();
        await container.StartAsync();

        var admin = container.GetConnectionString();
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT rolname FROM pg_roles", connection);
        var roles = new HashSet<string>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                roles.Add(reader.GetString(0));
        }

        return new Started(container, admin, roles);
    }

    private sealed record Started(PostgreSqlContainer Container, string AdminConnectionString, IReadOnlySet<string> BaselineRoles);
}
