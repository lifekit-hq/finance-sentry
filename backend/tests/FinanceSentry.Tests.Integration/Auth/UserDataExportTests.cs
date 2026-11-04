namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Text.Json;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

/// <summary>
/// The person's data export, against the real schema, seeded by the same catalog-driven seeder the erasure test uses:
/// one row per person-keyed table (and per table below one) for person A and for person B. A's export, over HTTP as A,
/// must hold exactly A's rows in every person-keyed table the exporters cover - and not one value that identifies B:
/// neither B's id nor the key of any row B owns. Tables are discovered from the catalog, so a person-keyed table added
/// later fails here until its exporter exists or it is added, with a reason, to <see cref="NotExported"/>.
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgresClusterStateCollection.Name)]
public sealed class UserDataExportTests : IAsyncLifetime
{
    // Person-keyed tables the export deliberately leaves out, each by the name the export would give it.
    private static readonly Dictionary<string, string> NotExported = new(StringComparer.Ordinal)
    {
        // Stored credentials: they let the holder act as the person, they are not data about the person.
        ["RefreshTokens"] = "session credential",
        ["McpAuthorizationCodes"] = "one-time credential",
        ["McpServiceTokens"] = "long-lived credential",
        ["AspNetUserLogins"] = "sign-in link to the identity provider",
        ["AspNetUserTokens"] = "Identity credential store",
        // Written by the Auth exporter under its own names (roles, permissionGrants).
        ["AspNetUserClaims"] = "exported as permissionGrants",
        ["AspNetUserRoles"] = "exported as roles",
        // The closure record is the audit trail, about the person but kept by the operator.
        ["AccountClosures"] = "operator audit trail",
    };

    private TestDatabase? _database;

    public async Task InitializeAsync() =>
        _database = await PostgresServer.Postgres14.CreateDatabaseAsync(resetsClusterRoles: true);

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    [DockerRequiredFact]
    public async Task Export_HoldsEveryRowOfThePersonAndNothingOfAnyoneElse()
    {
        await using var factory = new PostgresApiFactory(_database!.ConnectionString);
        factory.CreateClient().Dispose();

        var personA = Guid.NewGuid();
        var personB = Guid.NewGuid();
        await factory.CreateAccountAsync(personA);
        await factory.CreateAccountAsync(personB);

        await using var conn = new NpgsqlConnection(_database.ConnectionString);
        await conn.OpenAsync();
        var schema = await UserDataErasureTests.Schema.ReadAsync(conn);
        var seeded = await UserDataErasureTests.Seeder.SeedAsync(conn, schema, personA, personB);
        seeded.Unseedable.Should().BeEmpty("every person-keyed table must be seedable, or this test stops covering it");
        await MakeSeededRowsReadableAsync(factory.Services, conn);
        // The seeder leaves nullable columns null; a claim without a type would fail building A's principal.
        await using (var claims = new NpgsqlCommand(
            "UPDATE auth.\"AspNetUserClaims\" SET \"ClaimType\" = 'seed', \"ClaimValue\" = 'seed' WHERE \"ClaimType\" IS NULL", conn))
            await claims.ExecuteNonQueryAsync();

        using var client = await factory.ClientForAsync(personA);
        var response = await client.GetAsync("/api/v1/account/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        root.GetProperty("userId").GetGuid().Should().Be(personA);
        root.GetProperty("format").GetString().Should().Be("finance-sentry-export/1");

        // Nothing that identifies B: not the id, and not the key of any row B owns.
        text.Should().NotContainEquivalentOf(personB.ToString());
        foreach (var ((table, owner), keys) in seeded.Rows.Where(r => r.Key.Owner == personB.ToString()))
        {
            foreach (var key in keys.SelectMany(k => k).Where(k => k is Guid or string))
                text.Should().NotContainEquivalentOf(key!.ToString()!, $"B's {table} row key must not reach A's export (owner {owner})");
        }

        // Every person-keyed table the exporters cover holds exactly A's rows.
        var exported = ExportedTables(root);
        foreach (var table in schema.PersonKeyedTables)
        {
            var name = ExportName(table);
            if (NotExported.ContainsKey(name) || name == "AspNetUsers")
                continue;

            exported.Should().ContainKey(name, $"{table} is keyed by a person, so the export must cover it (or {nameof(NotExported)} say why not)");
            var expected = seeded.Rows.TryGetValue((table, personA.ToString()), out var rows) ? rows.Count : 0;
            exported[name].Should().Be(expected, $"the export of {table} holds A's rows only");
        }

        // A child row with no UserId of its own is written inside its owner: A's conversations carry A's messages.
        var conversations = root.GetProperty("modules").GetProperty("Agent").GetProperty("agent_conversations");
        conversations.GetArrayLength().Should().BeGreaterThan(0);
        conversations.EnumerateArray().Sum(c => c.GetProperty("messages").GetArrayLength()).Should().BeGreaterThan(0,
            "a conversation's messages have no person column and are exported with it");

        // The excluded credential tables never appear, whatever they are called.
        foreach (var name in NotExported.Keys)
            exported.Should().NotContainKey(name);
        exported.Should().ContainKey("users").WhoseValue.Should().Be(1);
    }

    [DockerRequiredFact]
    public async Task Export_RequiresASignedInPerson()
    {
        await using var factory = new PostgresApiFactory(_database!.ConnectionString);
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/account/export")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [DockerRequiredFact]
    public async Task Export_OfAPersonWithNothingStored_IsStillAWellFormedDocumentOfTheirOwnAccount()
    {
        await using var factory = new PostgresApiFactory(_database!.ConnectionString);
        factory.CreateClient().Dispose();
        var person = Guid.NewGuid();
        var other = Guid.NewGuid();
        await factory.CreateAccountAsync(person);
        await factory.CreateAccountAsync(other);

        using var client = await factory.ClientForAsync(person);
        var text = await (await client.GetAsync("/api/v1/account/export")).Content.ReadAsStringAsync();

        text.Should().NotContainEquivalentOf(other.ToString());
        using var document = JsonDocument.Parse(text);
        document.RootElement.GetProperty("modules").GetProperty("Auth").GetProperty("users")[0]
            .GetProperty("email").GetString().Should().Be($"{person}@test.local");
    }

    // The seeder fills a required column with a placeholder shaped by its SQL type: a random word for text, '{}' for
    // JSON. The export reads entities (the erasure's raw deletes never do), so where the model expects an enum name
    // or a JSON array the placeholder is replaced by one it can read.
    private static async Task MakeSeededRowsReadableAsync(IServiceProvider services, NpgsqlConnection conn)
    {
        using var scope = services.CreateScope();
        // Every module's assembly is loaded once the host has registered its services.
        var contexts = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("FinanceSentry.", StringComparison.Ordinal) == true)
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false } && typeof(DbContext).IsAssignableFrom(t))
            .Select(scope.ServiceProvider.GetService)
            .OfType<DbContext>();

        foreach (var context in contexts)
        {
            // The design-time model: it keeps the relational annotations (JSON containers, column names) the runtime
            // model drops.
            foreach (var entity in context.GetService<IDesignTimeModel>().Model.GetEntityTypes())
            {
                // An owned collection stored as one JSON column of its owner's table (it has no table mapping of its own).
                if (entity.IsMappedToJson() && entity.FindOwnership() is { IsUnique: false, PrincipalEntityType: var owner }
                    && entity.GetContainerColumnName() is { } container && owner.GetTableName() is { } ownerTable)
                {
                    var ownerQualified = owner.GetSchema() is { } ownerSchema ? $"\"{ownerSchema}\".\"{ownerTable}\"" : $"\"{ownerTable}\"";
                    await ExecuteAsync(conn, $"UPDATE {ownerQualified} SET \"{container}\" = '[]'::jsonb WHERE \"{container}\" = '{{}}'::jsonb");
                }

                if (entity.GetTableName() is not { } table || StoreObjectIdentifier.Create(entity, StoreObjectType.Table) is not { } storeObject)
                    continue;

                var qualified = entity.GetSchema() is { } schema ? $"\"{schema}\".\"{table}\"" : $"\"{table}\"";

                // A complex collection stored as one JSON column of this table.
                foreach (var complex in entity.GetDeclaredComplexProperties().Where(c => c.IsCollection))
                {
                    if (complex.ComplexType.GetContainerColumnName() is { } complexContainer)
                        await ExecuteAsync(conn, $"UPDATE {qualified} SET \"{complexContainer}\" = '[]'::jsonb WHERE \"{complexContainer}\" = '{{}}'::jsonb");
                }

                foreach (var property in PropertiesWithComplexOnes(entity))
                {
                    if (property.GetColumnName(storeObject) is not { } column)
                        continue;

                    // A list stored as JSON, whether by EF's collection mapping or by a value converter.
                    if (property.IsPrimitiveCollection || IsList(property.ClrType))
                    {
                        // Only a JSON column holds the object placeholder; a Postgres array's '{}' is already empty.
                        if (await ColumnTypeAsync(conn, entity.GetSchema(), table, column) is "jsonb")
                            await ExecuteAsync(conn, $"UPDATE {qualified} SET \"{column}\" = '[]'::jsonb WHERE \"{column}\" = '{{}}'::jsonb");
                        continue;
                    }

                    var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                    if (!type.IsEnum)
                        continue;

                    // An enum stored by number has an integer column; only a text column can hold a bad name. The column
                    // is the one this table names: an owned value object's carry its navigation's name (Health_State).
                    if (await ColumnTypeAsync(conn, entity.GetSchema(), table, column) is not ("character varying" or "text"))
                        continue;

                    var names = Enum.GetNames(type);
                    await using var fix = new NpgsqlCommand(
                        $"UPDATE {qualified} SET \"{column}\" = @first WHERE \"{column}\" IS NOT NULL AND \"{column}\" <> ALL(@names)", conn);
                    fix.Parameters.AddWithValue("first", names[0]);
                    fix.Parameters.AddWithValue("names", names);
                    await fix.ExecuteNonQueryAsync();
                }
            }
        }
    }

    // A sequence that serialises as a JSON array: not text, bytes or a map (which serialise as a string or an object).
    private static bool IsList(Type type) =>
        type != typeof(string) && type != typeof(byte[])
        && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
        && !typeof(System.Collections.IDictionary).IsAssignableFrom(type)
        && !type.GetInterfaces().Append(type).Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));

    private static async Task<object?> ColumnTypeAsync(NpgsqlConnection conn, string? schema, string table, string column)
    {
        await using var probe = new NpgsqlCommand(
            "SELECT data_type FROM information_schema.columns WHERE table_schema = coalesce(@schema, 'public') AND table_name = @table AND column_name = @column", conn);
        probe.Parameters.AddWithValue("schema", (object?)schema ?? DBNull.Value);
        probe.Parameters.AddWithValue("table", table);
        probe.Parameters.AddWithValue("column", column);
        return await probe.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // An entity's own properties plus those of its complex properties (a value object stored in the owner's columns).
    private static IEnumerable<IProperty> PropertiesWithComplexOnes(
        ITypeBase type) =>
        type.GetDeclaredProperties().Concat(type.GetDeclaredComplexProperties().SelectMany(c => PropertiesWithComplexOnes(c.ComplexType)));

    // Table name (without schema or quotes) -> row count, over every module's object.
    private static Dictionary<string, int> ExportedTables(JsonElement root)
    {
        var tables = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var module in root.GetProperty("modules").EnumerateObject())
        {
            foreach (var table in module.Value.EnumerateObject())
                tables[table.Name] = table.Value.GetArrayLength();
        }

        return tables;
    }

    private static string ExportName(string qualifiedTable) => qualifiedTable[(qualifiedTable.IndexOf('.') + 1)..].Trim('"');
}
