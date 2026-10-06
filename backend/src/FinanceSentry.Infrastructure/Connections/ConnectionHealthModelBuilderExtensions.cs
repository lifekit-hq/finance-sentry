namespace FinanceSentry.Infrastructure.Connections;

using System.Linq.Expressions;
using FinanceSentry.Core.Connections;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Query;

public static class ConnectionHealthModelBuilderExtensions
{
    private const int EnumNameMaxLength = 20;

    /// <summary>
    /// Maps a <see cref="ConnectionHealth"/> as a required owned type sharing the owner's row: its columns
    /// (<c>Health_State</c>, …) live in the owning module's schema. Owned rather than a complex type because
    /// the in-memory provider the contract tests run on cannot query complex types (EF Core 10.0.12).
    /// </summary>
    public static EntityTypeBuilder<TEntity> HasConnectionHealth<TEntity>(
        this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, ConnectionHealth?>> health)
        where TEntity : class
    {
        builder.OwnsOne(health, h =>
        {
            h.Property(x => x.State).HasConversion<string>().HasMaxLength(EnumNameMaxLength);
            h.Property(x => x.LastFailureClass).HasConversion<string>().HasMaxLength(EnumNameMaxLength);
            h.Property(x => x.LastFailureCode).HasMaxLength(ConnectionHealth.FailureCodeMaxLength);
        });
        builder.Navigation(health).IsRequired();
        return builder;
    }

    /// <summary>
    /// Sets every <see cref="ConnectionHealth"/> column in an <c>ExecuteUpdate</c>, so a job can store the
    /// health without touching the change tracker of the sync it observes.
    /// </summary>
    public static UpdateSettersBuilder<TEntity> SetConnectionHealth<TEntity>(
        this UpdateSettersBuilder<TEntity> setters, Expression<Func<TEntity, ConnectionHealth>> health, ConnectionHealth value) =>
        setters
            .SetProperty(Member(health, h => h.State), value.State)
            .SetProperty(Member(health, h => h.ConsecutiveFailures), value.ConsecutiveFailures)
            .SetProperty(Member(health, h => h.FirstFailureAt), value.FirstFailureAt)
            .SetProperty(Member(health, h => h.LastFailureAt), value.LastFailureAt)
            .SetProperty(Member(health, h => h.LastSuccessAt), value.LastSuccessAt)
            .SetProperty(Member(health, h => h.LastFailureClass), value.LastFailureClass)
            .SetProperty(Member(health, h => h.LastFailureCode), value.LastFailureCode)
            .SetProperty(Member(health, h => h.SuspectSince), value.SuspectSince)
            .SetProperty(Member(health, h => h.StateChangedAt), value.StateChangedAt);

    // e => e.Health composed with h => h.State gives e => e.Health.State.
    private static Expression<Func<TEntity, TProperty>> Member<TEntity, TProperty>(
        Expression<Func<TEntity, ConnectionHealth>> health, Expression<Func<ConnectionHealth, TProperty>> member) =>
        Expression.Lambda<Func<TEntity, TProperty>>(
            new ReplaceParameter(member.Parameters[0], health.Body).Visit(member.Body), health.Parameters);

    private sealed class ReplaceParameter(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == parameter ? replacement : base.VisitParameter(node);
    }
}
