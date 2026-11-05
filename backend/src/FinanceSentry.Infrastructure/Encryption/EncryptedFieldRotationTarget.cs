namespace FinanceSentry.Infrastructure.Encryption;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Rotates every field-encrypted column of one <typeparamref name="TContext"/> to the current key
/// (<see cref="ICredentialRotationTarget"/>, issue #493). The columns are found from the model - any property whose
/// value converter is an <see cref="IEncryptedFieldConverter"/> - so a module registers one target for the context with
/// <see cref="EncryptedFieldRotationExtensions.AddEncryptedFieldRotation{TContext}"/> and a new encrypted column is
/// covered without anyone remembering to add it.
/// <para>
/// A row is selected when any of its encrypted columns is not on the target key (<c>NOT LIKE 'v{target}.%'</c> in the
/// stored text), so a second run over a rotated store finds nothing and returns 0. It is then loaded and re-saved with
/// every encrypted column forced modified, which has the converter decrypt each under its recorded version and seal it
/// again under the current key: one UPDATE per row, each column's ciphertext, IV, tag and version together. A row that
/// cannot be decrypted throws and stops the run. The count is rows, not columns.
/// </para>
/// <para>Requires a relational provider and entities with a single <see cref="Guid"/> primary key.</para>
/// </summary>
public sealed class EncryptedFieldRotationTarget<TContext>(TContext db, string name) : ICredentialRotationTarget
    where TContext : DbContext
{
    private const int BatchSize = 200;

    private static readonly MethodInfo RotateEntityMethod = typeof(EncryptedFieldRotationTarget<TContext>)
        .GetMethod(nameof(RotateEntityAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    public string Name => name;

    public async Task<int> RotateAsync(int targetKeyVersion, CancellationToken cancellationToken)
    {
        var rotated = 0;
        foreach (var group in EncryptedColumns(db).GroupBy(column => column.Entity))
        {
            var columns = group.Select(column => column.Property).ToList();
            var rotate = (Task<int>)RotateEntityMethod.MakeGenericMethod(group.Key.ClrType)
                .Invoke(this, [group.Key, columns, targetKeyVersion, cancellationToken])!;
            rotated += await rotate;
        }

        return rotated;
    }

    /// <summary>Every field-encrypted column the context's model maps.</summary>
    public static IReadOnlyList<(IEntityType Entity, IProperty Property)> EncryptedColumns(DbContext context)
        => [.. context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties()
                .Where(property => property.GetValueConverter() is IEncryptedFieldConverter)
                .Select(property => (entity, property)))];

    private async Task<int> RotateEntityAsync<TEntity>(
        IEntityType entityType, IReadOnlyList<IProperty> columns, int targetKeyVersion, CancellationToken ct)
        where TEntity : class
    {
        var key = entityType.FindPrimaryKey()?.Properties;
        if (key is not { Count: 1 } || key[0].ClrType != typeof(Guid))
        {
            throw new InvalidOperationException(
                $"{entityType.DisplayName()} has an encrypted column but no single Guid primary key; rotation cannot address its rows.");
        }

        var table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
            ?? throw new InvalidOperationException($"{entityType.DisplayName()} is not mapped to a table.");
        var keyName = key[0].Name;
        var stale = string.Join(" OR ", columns.Select(property =>
        {
            var column = property.GetColumnName(table);
            return $"(\"{column}\" IS NOT NULL AND \"{column}\" <> '' AND \"{column}\" NOT LIKE {{0}})";
        }));
        var sql = $$"""
            SELECT "{{key[0].GetColumnName(table)}}" AS "Value"
            FROM "{{table.Schema ?? "public"}}"."{{table.Name}}"
            WHERE {{stale}}
            """;

        var ids = await db.Database
            .SqlQueryRaw<Guid>(sql, EncryptedField.LikePatternFor(targetKeyVersion))
            .ToListAsync(ct);

        foreach (var batch in ids.Chunk(BatchSize))
        {
            var batchIds = batch.ToList();
            var rows = await db.Set<TEntity>()
                .IgnoreQueryFilters()
                .Where(row => batchIds.Contains(EF.Property<Guid>(row, keyName)))
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                foreach (var column in columns)
                    db.Entry(row).Property(column.Name).IsModified = true;
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        return ids.Count;
    }
}

public static class EncryptedFieldRotationExtensions
{
    /// <summary>
    /// Registers the rotation target for every field-encrypted column of <typeparamref name="TContext"/>. Call it from the
    /// module registration that also registers the module's other <see cref="ICredentialRotationTarget"/>s.
    /// </summary>
    public static IServiceCollection AddEncryptedFieldRotation<TContext>(this IServiceCollection services, string name)
        where TContext : DbContext
        => services.AddScoped<ICredentialRotationTarget>(sp =>
            new EncryptedFieldRotationTarget<TContext>(sp.GetRequiredService<TContext>(), name));
}
