namespace FinanceSentry.Infrastructure.Persistence;

using System.Linq.Expressions;
using System.Reflection;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Deletes a person's rows from every <see cref="IUserOwned"/> entity a context maps, for account closure. Driven by
/// the model, the same way <see cref="OwnerScopedDbContext"/> applies the Owner filter, so a new per-user entity is
/// erased without anyone remembering to add it. A child entity with no <c>UserId</c> of its own (an Agent message)
/// goes with its owner through the database's cascade.
/// </summary>
public static class OwnedRowEraser
{
    private static readonly MethodInfo EraseEntityMethod = typeof(OwnedRowEraser)
        .GetMethod(nameof(EraseEntityAsync), BindingFlags.Static | BindingFlags.NonPublic)!;

    /// <summary>Deletes <paramref name="userId"/>'s rows from each owned root entity; returns the rows removed.</summary>
    public static async Task<int> EraseAsync(DbContext db, Guid userId, CancellationToken ct = default)
    {
        var removed = 0;
        foreach (var clrType in OwnedRootTypes(db))
        {
            var erase = (Task<int>)EraseEntityMethod.MakeGenericMethod(clrType).Invoke(null, [db, userId, ct])!;
            removed += await erase;
        }

        return removed;
    }

    /// <summary>The entity types <see cref="EraseAsync"/> deletes from, in the model's order.</summary>
    public static IReadOnlyList<Type> OwnedRootTypes(DbContext db) =>
    [
        .. db.Model.GetEntityTypes()
            .Where(e => typeof(IUserOwned).IsAssignableFrom(e.ClrType) && e.BaseType is null && !e.IsOwned())
            .Select(e => e.ClrType),
    ];

    /// <summary>
    /// Registers an <see cref="IUserDataEraser"/> for a context whose per-person data is all <see cref="IUserOwned"/>.
    /// </summary>
    public static IServiceCollection AddOwnedRowEraser<TContext>(this IServiceCollection services, string module)
        where TContext : DbContext
        => services.AddScoped<IUserDataEraser>(sp => new ContextEraser<TContext>(sp.GetRequiredService<TContext>(), module));

    private static Task<int> EraseEntityAsync<TEntity>(DbContext db, Guid userId, CancellationToken ct)
        where TEntity : class
    {
        // x => x.UserId == userId, built on the concrete type so EF sees a plain column comparison; the id is a
        // captured variable, so EF sends it as a parameter.
        Expression<Func<Guid>> person = () => userId;
        var entity = Expression.Parameter(typeof(TEntity), "x");
        var owned = Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(Expression.Property(entity, nameof(IUserOwned.UserId)), person.Body),
            entity);

        // Every filter off, not only the Owner one: a soft-deleted row (an archived transaction, a removed account) is
        // still the person's data. The predicate above is the only scope.
        return db.Set<TEntity>().IgnoreQueryFilters().Where(owned).ExecuteDeleteAsync(ct);
    }

    private sealed class ContextEraser<TContext>(TContext db, string module) : IUserDataEraser
        where TContext : DbContext
    {
        public string Module => module;

        public Task<int> EraseAsync(Guid userId, CancellationToken ct = default) => OwnedRowEraser.EraseAsync(db, userId, ct);
    }
}
