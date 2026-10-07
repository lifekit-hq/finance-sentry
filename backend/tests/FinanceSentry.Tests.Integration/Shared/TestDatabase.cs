namespace FinanceSentry.Tests.Integration.Shared;

using Npgsql;

/// <summary>
/// One test's database on a shared <see cref="PostgresServer"/>. Disposing it closes the test's pooled
/// connections and drops the database, terminating any session still attached to it.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly IReadOnlySet<string>? _baselineRoles;

    internal TestDatabase(string adminConnectionString, string name, IReadOnlySet<string>? baselineRoles)
    {
        _adminConnectionString = adminConnectionString;
        _baselineRoles = baselineRoles;
        Name = name;
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = name }.ConnectionString;
    }

    public string Name { get; }

    public string ConnectionString { get; }

    /// <summary>
    /// A further database on the same server for this test, not yet created (EF's <c>Migrate()</c> creates
    /// it), dropped with this one on dispose.
    /// </summary>
    public string CompanionConnectionString(string suffix) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = $"{Name}_{suffix}" }.ConnectionString;

    internal Task CreateAsync() => ExecuteAdminAsync($"CREATE DATABASE \"{Name}\"");

    public async ValueTask DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();

        foreach (var database in await ScalarsAsync(admin,
                     "SELECT datname FROM pg_database WHERE datname = @name OR datname LIKE @companions",
                     ("name", Name), ("companions", $"{Name}\\_%")))
        {
            NpgsqlConnection.ClearPool(new NpgsqlConnection(
                new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString));
            await ExecuteAsync(admin, $"DROP DATABASE \"{database}\" WITH (FORCE)");
        }

        if (_baselineRoles is null)
            return;

        foreach (var role in await ScalarsAsync(admin, "SELECT rolname FROM pg_roles"))
        {
            if (!_baselineRoles.Contains(role))
                await ExecuteAsync(admin, $"DROP ROLE \"{role}\"");
        }
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        await ExecuteAsync(admin, sql);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> ScalarsAsync(
        NpgsqlConnection connection, string sql, params (string Name, string Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values;
    }
}
