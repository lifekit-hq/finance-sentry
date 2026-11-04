namespace FinanceSentry.Infrastructure.Persistence;

using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Writes a person's rows from every <see cref="IUserOwned"/> entity a context maps, for the person's own data export.
/// The mirror of <see cref="OwnedRowEraser"/>: driven by the same model walk, so a new per-user entity is exported
/// without anyone remembering to add it, and scoped by the same explicit <c>UserId</c> predicate with every query
/// filter off (a removed account or an archived transaction is still the person's data).
/// <para>
/// A row is written as its mapped columns only: no navigation to another entity (a related row is written under its own
/// table), no computed property, and never a stored secret. Every <c>byte[]</c> column is dropped - the encrypted
/// provider keys, their IVs and auth tags - along with the few plaintext credential columns in <see cref="Redacted"/>
/// and the embedding vectors; <c>OwnedRowExporterTests</c> scans every mapped column in the solution for a
/// secret-looking name that is neither dropped nor reviewed, so a new credential column fails the build, not the privacy
/// notice. A child row with no <c>UserId</c> of its own (an Agent message) is written nested in its owner.
/// </para>
/// </summary>
public static class OwnedRowExporter
{
    // Rows buffered in the writer before the response is flushed to the client.
    private const int FlushEvery = 500;

    private static readonly MethodInfo ExportOwnedMethod = typeof(OwnedRowExporter)
        .GetMethod(nameof(ExportOwnedAsync), BindingFlags.Static | BindingFlags.NonPublic)!;

    /// <summary>
    /// Plaintext credential columns, as <c>EntityType.Property</c>: they hold a usable secret or bearer URL and are
    /// never written. (Encrypted columns are all <c>byte[]</c>, dropped by type.)
    /// </summary>
    private static readonly HashSet<string> Redacted =
    [
        "IBKRCredential.ConsumerKey",
        "IBKRCredential.AccessToken",
        "IBKRCredential.DhParam",
        "PushSubscription.Endpoint",
        "PushSubscription.P256dh",
        "PushSubscription.Auth",
    ];

    /// <summary>Writes <paramref name="userId"/>'s rows from each owned root entity, one array per table.</summary>
    public static async Task ExportAsync(DbContext db, Guid userId, Utf8JsonWriter writer, CancellationToken ct = default)
    {
        foreach (var clrType in OwnedRowEraser.OwnedRootTypes(db))
        {
            var export = (Task)ExportOwnedMethod.MakeGenericMethod(clrType).Invoke(null, [db, userId, writer, ct])!;
            await export;
        }
    }

    /// <summary>
    /// Writes the rows of <typeparamref name="TEntity"/> matching <paramref name="owned"/> as a property named for the
    /// table. For a module's own exporter, beside the owned-entity sweep (a table with no <c>UserId</c> column, or a
    /// nullable or string one): <paramref name="owned"/> must scope to the person, as it is the only filter.
    /// </summary>
    public static async Task WriteRowsAsync<TEntity>(
        DbContext db,
        Expression<Func<TEntity, bool>> owned,
        Utf8JsonWriter writer,
        CancellationToken ct = default,
        IReadOnlyCollection<string>? nestedCollections = null)
        where TEntity : class
    {
        nestedCollections ??= [];
        var options = OptionsFor(db.Model, nestedCollections.Select(n => $"{typeof(TEntity).Name}.{n}"));

        IQueryable<TEntity> query = db.Set<TEntity>().IgnoreQueryFilters().AsNoTracking().Where(owned);
        foreach (var navigation in nestedCollections)
            query = query.Include(navigation);

        var table = TableName(db.Model, typeof(TEntity));
        writer.WritePropertyName(table);
        writer.WriteStartArray();
        var written = 0;
        try
        {
            await foreach (var row in query.AsAsyncEnumerable().WithCancellation(ct))
            {
                WriteJson(writer, row, typeof(TEntity), options);
                if (++written % FlushEvery == 0)
                    await writer.FlushAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Names the table in the log: a row the model cannot read would otherwise surface as a bare EF error.
            throw new InvalidOperationException($"Exporting {table} failed after {written} rows.", ex);
        }

        writer.WriteEndArray();
        await writer.FlushAsync(ct);
    }

    /// <summary>
    /// Writes <paramref name="value"/> as the next JSON value. Serialises to bytes first because
    /// <c>JsonSerializer.Serialize(Utf8JsonWriter, ...)</c> flushes the writer synchronously, which ASP.NET Core refuses
    /// on a response body; here the writer only buffers and the caller's <c>FlushAsync</c> does the I/O.
    /// </summary>
    public static void WriteJson(Utf8JsonWriter writer, object? value, Type type, JsonSerializerOptions options)
        => writer.WriteRawValue(JsonSerializer.SerializeToUtf8Bytes(value, type, options), skipInputValidation: true);

    /// <summary>
    /// The column names written for <paramref name="entityType"/> - what a reader of the export sees. For the test that
    /// no secret-looking column is exported.
    /// </summary>
    public static IReadOnlyList<string> ExportedColumns(IModel model, Type entityType)
    {
        var info = OptionsFor(model, NestedCollectionsOf(model, entityType)).GetTypeInfo(entityType);
        return [.. info.Properties.Select(p => (p.AttributeProvider as MemberInfo)?.Name ?? p.Name)];
    }

    /// <summary>
    /// Registers an <see cref="IUserDataExporter"/> for a context whose per-person data is all
    /// <see cref="IUserOwned"/>, the counterpart of <see cref="OwnedRowEraser.AddOwnedRowEraser{TContext}"/>.
    /// </summary>
    public static IServiceCollection AddOwnedRowExporter<TContext>(this IServiceCollection services, string module)
        where TContext : DbContext
        => services.AddScoped<IUserDataExporter>(sp => new ContextExporter<TContext>(sp.GetRequiredService<TContext>(), module));

    private static async Task ExportOwnedAsync<TEntity>(DbContext db, Guid userId, Utf8JsonWriter writer, CancellationToken ct)
        where TEntity : class
        => await WriteRowsAsync(
            db,
            OwnedRowEraser.OwnedBy<TEntity>(userId),
            writer,
            ct,
            NestedCollectionsOf(db.Model, typeof(TEntity)));

    // Child rows that exist only under this entity: a collection navigation to an entity with no UserId of its own, whose
    // rows the database deletes with the parent. Anything else related is written under its own table.
    private static List<string> NestedCollectionsOf(IModel model, Type entityType) =>
        model.FindEntityType(entityType)?.GetNavigations()
            .Where(n => n is { IsCollection: true, IsOnDependent: false }
                && !n.ForeignKey.IsOwnership
                && n.ForeignKey.DeleteBehavior == DeleteBehavior.Cascade
                && !typeof(IUserOwned).IsAssignableFrom(n.TargetEntityType.ClrType))
            .Select(n => n.Name)
            .ToList()
        ?? [];

    private static string TableName(IModel model, Type entityType)
    {
        var entity = model.FindEntityType(entityType);
        return entity?.GetTableName() ?? entityType.Name;
    }

    private static JsonSerializerOptions OptionsFor(IModel model, IEnumerable<string> nested)
    {
        var keptNavigations = nested.ToHashSet(StringComparer.Ordinal);
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { typeInfo => Strip(typeInfo, model, keptNavigations) },
            },
        };
    }

    private static void Strip(JsonTypeInfo typeInfo, IModel model, HashSet<string> keptNavigations)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        var entity = model.FindEntityType(typeInfo.Type);
        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            var property = typeInfo.Properties[i];
            var name = (property.AttributeProvider as MemberInfo)?.Name ?? property.Name;
            if (property.PropertyType == typeof(byte[]) || property.PropertyType == typeof(float[])
                || Redacted.Contains($"{typeInfo.Type.Name}.{name}")
                || (entity is not null && !IsExportable(entity, name, keptNavigations)))
                typeInfo.Properties.RemoveAt(i);
        }
    }

    // A mapped column, an owned value (written inline), or a child collection the exporter loads; not a link back to a
    // parent, not a navigation to another table's rows, and not a property EF does not map.
    private static bool IsExportable(IEntityType entity, string name, HashSet<string> keptNavigations)
    {
        if (entity.FindProperty(name) is not null || entity.FindComplexProperty(name) is not null)
            return true;

        return entity.FindNavigation(name) is { IsOnDependent: false } navigation
            && (navigation.ForeignKey.IsOwnership || keptNavigations.Contains($"{entity.ClrType.Name}.{name}"));
    }

    private sealed class ContextExporter<TContext>(TContext db, string module) : IUserDataExporter
        where TContext : DbContext
    {
        public string Module => module;

        public Task ExportAsync(Guid userId, Utf8JsonWriter writer, CancellationToken ct = default)
            => OwnedRowExporter.ExportAsync(db, userId, writer, ct);
    }
}
