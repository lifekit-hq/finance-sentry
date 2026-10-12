namespace FinanceSentry.Infrastructure.Persistence;

using System.Linq.Expressions;
using System.Reflection;
using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Base for a per-user DbContext. After the derived context has described its model, every
/// <see cref="IUserOwned"/> entity gets the <see cref="OwnerQueryFilter"/> from the one <see cref="CurrentUserId"/>
/// source, and the shared column conventions (<see cref="PersistenceConventions"/>) apply. Background jobs run with no
/// person in scope, which makes the filter match nothing - a job that legitimately reads across users opts out with
/// <c>IgnoreQueryFilters([OwnerQueryFilter.Name])</c>.
/// </summary>
public abstract class OwnerScopedDbContext(DbContextOptions options, ICurrentUser currentUser) : DbContext(options)
{
    private static readonly PropertyInfo CurrentUserIdProperty = typeof(OwnerScopedDbContext)
        .GetProperty(nameof(CurrentUserId), BindingFlags.Instance | BindingFlags.NonPublic)!;

    // Read by the Owner query filter on every query this context runs; null (no person in scope) matches no row.
    protected Guid? CurrentUserId => currentUser.UserId;

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureModel(modelBuilder);
        ApplyOwnerFilters(modelBuilder);
    }

    /// <summary>Describes the module's entities; the Owner filter is added afterwards, never declared here.</summary>
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        PersistenceConventions.Apply(configurationBuilder);
    }

    private void ApplyOwnerFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // A filter is declared on the hierarchy root only; an owned type is filtered through its owner.
            if (!typeof(IUserOwned).IsAssignableFrom(entityType.ClrType) || entityType.BaseType is not null || entityType.IsOwned())
                continue;

            entityType.SetQueryFilter(OwnerQueryFilter.Name, OwnerFilter(entityType.ClrType));
        }
    }

    // x => x.UserId == this.CurrentUserId, the shape EF rebinds to the executing context's instance per query.
    private LambdaExpression OwnerFilter(Type entityClrType)
    {
        var entity = Expression.Parameter(entityClrType, "x");
        var owner = Expression.Convert(Expression.Property(entity, nameof(IUserOwned.UserId)), typeof(Guid?));
        var acting = Expression.Property(Expression.Constant(this), CurrentUserIdProperty);
        return Expression.Lambda(Expression.Equal(owner, acting), entity);
    }
}
