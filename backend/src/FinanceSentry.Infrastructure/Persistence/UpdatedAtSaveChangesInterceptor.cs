namespace FinanceSentry.Infrastructure.Persistence;

using FinanceSentry.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Stamps <c>UpdatedAt</c> with the current time on every <see cref="IHasUpdatedAt"/> entity saved in
/// the Modified state. Added entities keep whatever their constructor or initializer set.
/// </summary>
public sealed class UpdatedAtSaveChangesInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    private const string UpdatedAtProperty = "UpdatedAt";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
            return;

        var now = timeProvider.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries<IHasUpdatedAt>())
        {
            if (entry.State != EntityState.Modified)
                continue;

            var property = entry.Metadata.FindProperty(UpdatedAtProperty);
            if (property is null)
                continue;

            var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            // Both branches are cast to object: a bare ternary would convert the DateTime to DateTimeOffset.
            entry.Property(UpdatedAtProperty).CurrentValue = clrType == typeof(DateTimeOffset)
                ? (object)now
                : now.UtcDateTime;
        }
    }
}
