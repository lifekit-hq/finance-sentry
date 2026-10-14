namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Jobs;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

/// <summary>
/// Account closure's erasure, against the real schema: the API migrates a fresh database, then every table the
/// schema keys by a person (a <c>UserId</c> column) or hangs off such a table by a foreign key gets one row for
/// person A, one for person B, and - where the person column is nullable - one shared row. Running every
/// <see cref="IUserDataEraser"/> for A and deleting A's Identity account must remove each of A's rows but the closure
/// record (the audit trail) and leave every one of B's and the shared corpus untouched. The tables are discovered
/// from the catalog, not listed, so a table added later is covered without anyone editing this test.
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgresClusterStateCollection.Name)]
public sealed class UserDataErasureTests : IAsyncLifetime
{
    private const string Shared = "shared";

    // Schemas holding no person's data: Postgres's own, and the job store (in-memory under Testing anyway).
    private static readonly string[] SkippedSchemas = ["pg_catalog", "information_schema", "pg_toast", "hangfire"];

    // Kept on purpose: the closure record is the audit trail and outlives the account (no foreign key to it).
    private static readonly string[] RetainedTables = ["auth.\"AccountClosures\""];

    // What closing deletes at once, before the grace period: every stored provider key, push endpoint and sign-in link.
    private static readonly string[] CredentialTables =
    [
        "auth.\"AspNetUserLogins\"",
        "bank_sync.\"MonobankCredentials\"",
        "bank_sync.\"TrueLayerConnections\"",
        "brokerage_sync.\"IBKRCredentials\"",
        "brokerage_sync.\"IBKRFlexCredentials\"",
        "brokerage_sync.\"InzhurCredentials\"",
        "companion.push_subscriptions",
        "crypto_sync.\"ExchangeCredentials\"",
    ];

    private TestDatabase? _database;

    public async Task InitializeAsync() =>
        _database = await PostgresServer.Postgres14.CreateDatabaseAsync(resetsClusterRoles: true);

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    [DockerRequiredFact]
    public async Task ErasingOnePerson_RemovesEveryRowTheyOwn_AndLeavesTheOtherPersonAndTheSharedCorpus()
    {
        await using var factory = new PostgresApiFactory(_database!.ConnectionString);
        factory.CreateClient().Dispose();

        var personA = Guid.NewGuid();
        var personB = Guid.NewGuid();
        await factory.CreateAccountAsync(personA);
        await factory.CreateAccountAsync(personB);

        await using var conn = new NpgsqlConnection(_database.ConnectionString);
        await conn.OpenAsync();

        var schema = await Schema.ReadAsync(conn);
        var seeded = await SeedAsync(conn, schema, personA, personB);

        var firstPass = await EraseAsync(factory.Services, personA);

        await AssertOnlyFirstPersonErasedAsync(conn, schema, seeded, personA, personB);
        firstPass.Should().BeGreaterThan(0);
        (await EraseAsync(factory.Services, personA)).Should().Be(0, "erasure is idempotent: a second run finds nothing");
    }

    /// <summary>
    /// The whole closure, end to end: the person closes their own account through the API, which at once ends their
    /// session and deletes the provider keys they stored while the rest of their data stays for the grace period;
    /// once the grace period has ended the purge job erases everything they own and deletes the account, keeps the
    /// closure record as the audit trail, and leaves the other person's data (keys included) untouched.
    /// </summary>
    [DockerRequiredFact]
    public async Task ClosingThenPurging_RemovesKeysAtOnceAndEverythingElseAfterTheGracePeriod_OnlyForTheClosedAccount()
    {
        await using var factory = new PostgresApiFactory(_database!.ConnectionString);
        factory.CreateClient().Dispose();

        var personA = Guid.NewGuid();
        var personB = Guid.NewGuid();
        await factory.CreateAccountAsync(personA);
        await factory.CreateAccountAsync(personB);

        await using var conn = new NpgsqlConnection(_database.ConnectionString);
        await conn.OpenAsync();

        var schema = await Schema.ReadAsync(conn);
        var seeded = await SeedAsync(conn, schema, personA, personB);

        // The seeder writes a placeholder closure for each person; this test makes its own.
        await using (var clear = new NpgsqlCommand("DELETE FROM auth.\"AccountClosures\"", conn))
            await clear.ExecuteNonQueryAsync();
        // The seeder leaves nullable columns null; a claim without a type would fail building A's principal.
        await using (var claims = new NpgsqlCommand(
            "UPDATE auth.\"AspNetUserClaims\" SET \"ClaimType\" = 'seed', \"ClaimValue\" = 'seed' WHERE \"ClaimType\" IS NULL", conn))
            await claims.ExecuteNonQueryAsync();
        seeded =seeded with { Rows = seeded.Rows.Where(r => !RetainedTables.Contains(r.Key.Table)).ToDictionary() };

        // An Owner besides the seeded people, so closing A is never refused as the last Owner's.
        await factory.CreateAccountAsync(Guid.NewGuid(), AuthRoles.Owner);
        using var client = await factory.ClientForAsync(personA);
        (await client.GetAsync("/api/v1/account/closure")).StatusCode.Should().Be(HttpStatusCode.OK);

        var wrong = await client.PostAsJsonAsync("/api/v1/account/close", new { confirmEmail = "someone-else@test.local" });
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrong.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("CLOSURE_CONFIRMATION_MISMATCH");

        var close = await client.PostAsJsonAsync("/api/v1/account/close", new { confirmEmail = $" {personA}@TEST.local " });
        close.StatusCode.Should().Be(HttpStatusCode.OK);
        var closure = (await close.Content.ReadFromJsonAsync<ClosureShape>())!;
        (closure.PurgeDueAt - closure.ClosedAt).Should().Be(TimeSpan.FromDays(30));

        (await client.GetAsync("/api/v1/account/closure")).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the session ends with the closure");

        foreach (var table in CredentialTables)
        {
            schema.Tables.Should().ContainKey(table);
            (await schema.CountOwnedAsync(conn, table, personA)).Should().Be(0, $"closing deletes the closed account's {table} at once");
            (await schema.CountOwnedAsync(conn, table, personB)).Should().BeGreaterThan(0, $"the other person's {table} stay");
        }

        const string liveRefreshTokens = "SELECT count(*) FROM auth.\"RefreshTokens\" WHERE \"UserId\" = @p AND NOT \"IsRevoked\"";
        (await CountAsync(conn, liveRefreshTokens, personA.ToString())).Should().Be(0, "every refresh token of the closed account is revoked");
        (await CountAsync(conn, liveRefreshTokens, personB.ToString())).Should().BeGreaterThan(0);
        (await schema.CountOwnedAsync(conn, "bank_sync.\"BankAccounts\"", personA))
            .Should().BeGreaterThan(0, "the rest of the data stays, reopenable, during the grace period");

        const string account = "SELECT count(*) FROM auth.\"AspNetUsers\" WHERE \"Id\" = @p";
        await RunPurgeAsync(factory.Services);
        (await CountAsync(conn, account, personA.ToString())).Should().Be(1, "nothing is purged before the grace period ends");

        await using (var due = new NpgsqlCommand(
            "UPDATE auth.\"AccountClosures\" SET \"PurgeDueAt\" = now() - interval '1 minute' WHERE \"UserId\" = @p", conn))
        {
            due.Parameters.AddWithValue("p", personA);
            (await due.ExecuteNonQueryAsync()).Should().Be(1);
        }

        await RunPurgeAsync(factory.Services);

        await AssertOnlyFirstPersonErasedAsync(conn, schema, seeded, personA, personB);
        (await CountAsync(conn, account, personA.ToString())).Should().Be(0);
        (await CountAsync(conn, account, personB.ToString())).Should().Be(1);

        var record = await ReadClosureAsync(conn, personA);
        record.PurgedAt.Should().NotBeNull("the closure record outlives the account as its audit trail");
        record.ErasedRows.Should().Contain("\"Account\": 1");
        record.CredentialRevocation.Should().Contain("BankSync");

        await RunPurgeAsync(factory.Services);
        (await ReadClosureAsync(conn, personA)).Should().Be(record, "a purged closure is never picked up again");
    }

    private static async Task<int> EraseAsync(IServiceProvider services, Guid userId)
    {
        using var scope = services.CreateScope();
        var removed = 0;
        foreach (var eraser in scope.ServiceProvider.GetServices<IUserDataEraser>())
            removed += await eraser.EraseAsync(userId);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByIdAsync(userId.ToString()) is { } user)
        {
            (await users.DeleteAsync(user)).Succeeded.Should().BeTrue();
            removed++;
        }

        return removed;
    }

    private static async Task<SeedResult> SeedAsync(NpgsqlConnection conn, Schema schema, Guid personA, Guid personB)
    {
        var seeded = await Seeder.SeedAsync(conn, schema, personA, personB);

        seeded.Unseedable.Should().BeEmpty(
            "every person-keyed table must be seedable here, or this test silently stops covering it");
        seeded.Rows.Keys.Select(k => k.Table).Distinct().Should().HaveCountGreaterThan(30,
            "the sweep must reach the whole schema, not a handful of tables");
        return seeded;
    }

    private static async Task AssertOnlyFirstPersonErasedAsync(
        NpgsqlConnection conn, Schema schema, SeedResult seeded, Guid personA, Guid personB)
    {
        foreach (var ((table, owner), keys) in seeded.Rows)
        {
            foreach (var key in keys)
            {
                var exists = await schema.RowExistsAsync(conn, table, key);
                if (RetainedTables.Contains(table))
                    exists.Should().BeTrue($"{table} row {Describe(key)} is a closure's audit record");
                else if (owner == personA.ToString())
                    exists.Should().BeFalse($"{table} row {Describe(key)} belonged to the closed account");
                else
                    exists.Should().BeTrue($"{table} row {Describe(key)} belongs to {(owner == Shared ? "the shared corpus" : "another person")}");
            }
        }

        foreach (var table in schema.PersonKeyedTables.Except(RetainedTables))
        {
            (await schema.CountOwnedAsync(conn, table, personA)).Should().Be(0, $"no row of {table} may still name the closed account");
            (await schema.CountOwnedAsync(conn, table, personB)).Should().BeGreaterThan(0, $"the other person's {table} rows stay");
        }
    }

    private static async Task RunPurgeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AccountPurgeJob>().ExecuteAsync();
    }

    private static async Task<ClosureRecord> ReadClosureAsync(NpgsqlConnection conn, Guid person)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT "PurgedAt", "ErasedRows"::text, "CredentialRevocation"::text FROM auth."AccountClosures"
            WHERE "UserId" = @p ORDER BY "ClosedAt" DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("p", person);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("the closure is recorded");
        return new ClosureRecord(
            reader.IsDBNull(0) ? null : reader.GetDateTime(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private static async Task<long> CountAsync(NpgsqlConnection conn, string sql, string person)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("p", person);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private sealed record ClosureShape(DateTime ClosedAt, DateTime PurgeDueAt);

    private sealed record ClosureRecord(DateTime? PurgedAt, string? ErasedRows, string? CredentialRevocation);

    private sealed record ErrorShape(string Error, string ErrorCode);

    private static string Describe(object?[] key) => $"({string.Join(", ", key)})";

    private sealed record Column(string Name, string Type, bool NotNull, bool HasDefault, char Category);

    private sealed record ForeignKey(string Column, string Parent, string ParentColumn);

    private sealed record Table(string Name, IReadOnlyList<Column> Columns, IReadOnlyList<string> PrimaryKey, IReadOnlyList<ForeignKey> ForeignKeys)
    {
        public Column? PersonColumn => Columns.FirstOrDefault(c => c.Name == "UserId");
    }

    private sealed class Schema(IReadOnlyDictionary<string, Table> tables)
    {
        public IReadOnlyDictionary<string, Table> Tables => tables;

        public IEnumerable<string> PersonKeyedTables => tables.Values.Where(t => t.PersonColumn is not null).Select(t => t.Name);

        public static async Task<Schema> ReadAsync(NpgsqlConnection conn)
        {
            var columns = new Dictionary<string, List<Column>>();
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT format('%I.%I', n.nspname, c.relname), a.attname, format_type(a.atttypid, a.atttypmod), a.attnotnull,
                       ad.adbin IS NOT NULL OR a.attidentity <> '' OR a.attgenerated <> '', t.typcategory
                FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_type t ON t.oid = a.atttypid
                LEFT JOIN pg_attrdef ad ON ad.adrelid = a.attrelid AND ad.adnum = a.attnum
                WHERE c.relkind IN ('r', 'p') AND a.attnum > 0 AND NOT a.attisdropped AND NOT (n.nspname = ANY(@skipped))
                ORDER BY 1, a.attnum
                """, conn))
            {
                cmd.Parameters.AddWithValue("skipped", SkippedSchemas);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var table = reader.GetString(0);
                    if (!columns.TryGetValue(table, out var list))
                        columns[table] = list = [];
                    list.Add(new Column(reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetBoolean(4), reader.GetChar(5)));
                }
            }

            var primaryKeys = new Dictionary<string, List<string>>();
            var foreignKeys = new Dictionary<string, List<ForeignKey>>();
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT con.contype, format('%I.%I', n.nspname, c.relname), a.attname,
                       CASE WHEN con.contype = 'f' THEN format('%I.%I', fn.nspname, fc.relname) END, fa.attname
                FROM pg_constraint con
                JOIN pg_class c ON c.oid = con.conrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                CROSS JOIN LATERAL unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord)
                JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum
                LEFT JOIN pg_class fc ON fc.oid = con.confrelid
                LEFT JOIN pg_namespace fn ON fn.oid = fc.relnamespace
                LEFT JOIN pg_attribute fa ON fa.attrelid = con.confrelid AND fa.attnum = con.confkey[k.ord]
                WHERE con.contype IN ('p', 'f') AND NOT (n.nspname = ANY(@skipped))
                ORDER BY 2, con.conname, k.ord
                """, conn))
            {
                cmd.Parameters.AddWithValue("skipped", SkippedSchemas);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var table = reader.GetString(1);
                    if (reader.GetChar(0) == 'p')
                    {
                        if (!primaryKeys.TryGetValue(table, out var pk))
                            primaryKeys[table] = pk = [];
                        pk.Add(reader.GetString(2));
                    }
                    else
                    {
                        if (!foreignKeys.TryGetValue(table, out var fks))
                            foreignKeys[table] = fks = [];
                        fks.Add(new ForeignKey(reader.GetString(2), reader.GetString(3), reader.GetString(4)));
                    }
                }
            }

            return new Schema(columns.ToDictionary(
                kv => kv.Key,
                kv => new Table(
                    kv.Key,
                    kv.Value,
                    primaryKeys.GetValueOrDefault(kv.Key) ?? [],
                    foreignKeys.GetValueOrDefault(kv.Key) ?? [])));
        }

        public async Task<bool> RowExistsAsync(NpgsqlConnection conn, string table, object?[] key)
        {
            var pk = tables[table].PrimaryKey;
            await using var cmd = new NpgsqlCommand(
                $"SELECT count(*) FROM {table} WHERE {string.Join(" AND ", pk.Select((c, i) => $"\"{c}\" = @k{i}"))}", conn);
            for (var i = 0; i < pk.Count; i++)
                cmd.Parameters.AddWithValue($"k{i}", key[i]!);
            return (long)(await cmd.ExecuteScalarAsync())! > 0;
        }

        public async Task<long> CountOwnedAsync(NpgsqlConnection conn, string table, Guid person)
        {
            var column = tables[table].PersonColumn!;
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE \"{column.Name}\"::text = @person", conn);
            cmd.Parameters.AddWithValue("person", person.ToString());
            return (long)(await cmd.ExecuteScalarAsync())!;
        }
    }

    private sealed record SeedResult(
        IReadOnlyDictionary<(string Table, string Owner), List<object?[]>> Rows, IReadOnlyList<string> Unseedable);

    /// <summary>
    /// Inserts one row per owner into each person-keyed table and each table reachable from one by a foreign key, in
    /// dependency order. A foreign key to a seeded table points at the same owner's row; one to a table nobody owns
    /// (roles, categories) takes any existing row. Columns with a default are left to it; other nullable columns
    /// are null; required ones get a type-shaped placeholder.
    /// </summary>
    private static class Seeder
    {
        private const string UsersTable = "auth.\"AspNetUsers\"";

        public static async Task<SeedResult> SeedAsync(NpgsqlConnection conn, Schema schema, Guid personA, Guid personB)
        {
            var owners = new[] { personA.ToString(), personB.ToString(), Shared };
            var rows = new Dictionary<(string, string), List<object?[]>>();

            // The accounts themselves exist already (created through Identity); their rows are the users' roots.
            foreach (var owner in owners.Take(2))
                rows[(UsersTable, owner)] = [[owner]];

            var inScope = Closure(schema);
            var pending = inScope.Where(t => t != UsersTable).ToHashSet();
            var unseedable = new List<string>();

            while (pending.Count > 0)
            {
                var ready = pending
                    .Where(t => schema.Tables[t].ForeignKeys.All(fk => fk.Parent == t || !pending.Contains(fk.Parent)))
                    .OrderBy(t => t, StringComparer.Ordinal)
                    .ToList();
                if (ready.Count == 0)
                {
                    unseedable.AddRange(pending.Select(t => $"{t}: foreign-key cycle"));
                    break;
                }

                foreach (var table in ready)
                {
                    pending.Remove(table);
                    foreach (var owner in owners)
                    {
                        var outcome = await InsertAsync(conn, schema.Tables[table], owner, rows);
                        if (outcome is { } reason)
                            unseedable.Add($"{table} ({owner}): {reason}");
                    }
                }
            }

            await SeedUnlinkedChildrenAsync(conn, owners, rows);

            return new SeedResult(rows, unseedable);
        }

        // Person-keyed tables, then every table that reaches one by a foreign key, to a fixed point.
        private static HashSet<string> Closure(Schema schema)
        {
            var set = schema.PersonKeyedTables.Append(UsersTable).ToHashSet();
            bool grew;
            do
            {
                grew = false;
                foreach (var table in schema.Tables.Values)
                {
                    if (!set.Contains(table.Name) && table.ForeignKeys.Any(fk => set.Contains(fk.Parent)))
                        grew = set.Add(table.Name);
                }
            }
            while (grew);

            return set;
        }

        /// <summary>Returns null on success, or why the row could not be written for this owner.</summary>
        private static async Task<string?> InsertAsync(
            NpgsqlConnection conn, Table table, string owner, Dictionary<(string, string), List<object?[]>> rows)
        {
            var person = table.PersonColumn;
            var isShared = owner == Shared;

            // The shared corpus exists only where the person column is nullable, and below such rows.
            if (isShared && person is { NotNull: true })
                return null;

            var names = new List<string>();
            var values = new List<string>();
            var parameters = new List<NpgsqlParameter>();
            var hasOwnerLink = person is not null;

            foreach (var column in table.Columns)
            {
                var fk = table.ForeignKeys.FirstOrDefault(f => f.Column == column.Name);
                string? value;

                if (column == person)
                {
                    value = isShared ? null : Param(parameters, column.Type == "uuid" ? Guid.Parse(owner) : owner);
                }
                else if (fk is not null && rows.TryGetValue((fk.Parent, owner), out var parentRows))
                {
                    value = Param(parameters, await ParentKeyAsync(conn, fk, parentRows[0]));
                    hasOwnerLink = true;
                }
                else if (fk is not null && fk.Parent != table.Name && !IsOwnedAnywhere(rows, fk.Parent))
                {
                    var any = await AnyKeyAsync(conn, fk);
                    if (any is null && column.NotNull)
                        return $"no {fk.Parent} row to point {column.Name} at";
                    value = any is null ? null : Param(parameters, any);
                }
                else if (fk is not null)
                {
                    // A parent other owners have but this one does not (the shared corpus below a person-only table).
                    if (column.NotNull)
                        return null;
                    value = null;
                }
                else if (column.HasDefault || !column.NotNull)
                {
                    continue;
                }
                else
                {
                    value = Placeholder(column);
                    if (value is null)
                        return $"no placeholder for {column.Name} {column.Type}";
                }

                names.Add($"\"{column.Name}\"");
                values.Add(value is null ? "NULL" : $"CAST({value} AS {column.Type})");
            }

            if (!hasOwnerLink)
                return null;

            var returning = string.Join(", ", table.PrimaryKey.Select(c => $"\"{c}\""));
            await using var cmd = new NpgsqlCommand(
                $"INSERT INTO {table.Name} ({string.Join(", ", names)}) VALUES ({string.Join(", ", values)}) RETURNING {returning}",
                conn);
            cmd.Parameters.AddRange(parameters.ToArray());

            try
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                await reader.ReadAsync();
                var key = new object[reader.FieldCount];
                reader.GetValues(key);
                if (!rows.TryGetValue((table.Name, owner), out var list))
                    rows[(table.Name, owner)] = list = [];
                list.Add(key);
                return null;
            }
            catch (PostgresException ex)
            {
                return ex.MessageText;
            }
        }

        // Candidate scores carry their candidate's id with no foreign key, so the catalog cannot tie them to an owner.
        private static async Task SeedUnlinkedChildrenAsync(
            NpgsqlConnection conn, IEnumerable<string> owners, Dictionary<(string, string), List<object?[]>> rows)
        {
            foreach (var owner in owners)
            {
                if (!rows.TryGetValue(("research.opportunity_candidates", owner), out var candidates))
                    continue;

                await using var cmd = new NpgsqlCommand(
                    """
                    INSERT INTO research.candidate_scores ("Id", "CandidateId", "CrowdingClass", "FormulaVersion", "IpsFit", "Evidence")
                    VALUES (gen_random_uuid(), @candidate, 'Normal', 1, '{}', '{}') RETURNING "Id"
                    """, conn);
                cmd.Parameters.AddWithValue("candidate", candidates[0][0]!);
                var id = await cmd.ExecuteScalarAsync();
                rows[("research.candidate_scores", owner)] = [[id]];
            }
        }

        private static bool IsOwnedAnywhere(Dictionary<(string Table, string Owner), List<object?[]>> rows, string table) =>
            rows.Keys.Any(k => k.Table == table);

        private static async Task<object?> ParentKeyAsync(NpgsqlConnection conn, ForeignKey fk, object?[] parentKey)
        {
            // The referenced column is almost always the parent's single-column primary key, which the row key holds.
            if (parentKey.Length == 1)
                return parentKey[0];

            await using var cmd = new NpgsqlCommand($"SELECT \"{fk.ParentColumn}\" FROM {fk.Parent} LIMIT 1", conn);
            return await cmd.ExecuteScalarAsync();
        }

        private static async Task<object?> AnyKeyAsync(NpgsqlConnection conn, ForeignKey fk)
        {
            await using var cmd = new NpgsqlCommand($"SELECT \"{fk.ParentColumn}\" FROM {fk.Parent} LIMIT 1", conn);
            return await cmd.ExecuteScalarAsync();
        }

        private static string Param(List<NpgsqlParameter> parameters, object? value)
        {
            var name = $"p{parameters.Count}";
            parameters.Add(new NpgsqlParameter(name, value ?? DBNull.Value));
            return $"@{name}";
        }

        // A literal Postgres can cast to the column's type; null when the type has no obvious placeholder.
        private static string? Placeholder(Column column) => column.Type switch
        {
            "uuid" => "gen_random_uuid()",
            "jsonb" or "json" => "'{}'",
            "bytea" => "'\\x00'",
            _ when column.Type.EndsWith("[]", StringComparison.Ordinal) => "'{}'",
            _ => column.Category switch
            {
                'S' => $"'{Guid.NewGuid():N}'",
                'N' => "0",
                'B' => "false",
                'D' => "now()",
                'T' => "'0 seconds'",
                _ => null,
            },
        };
    }
}
