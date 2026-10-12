namespace FinanceSentry.Modules.Retention.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>Design-time factory so <c>dotnet ef migrations</c> can build the context outside the host.</summary>
public class RetentionDbContextFactory() : DesignTimeDbContextFactoryBase<RetentionDbContext>("__ef_migrations_history_retention")
{
    // The context holds no user data, so the base's (absent) person has nothing to scope.
    protected override RetentionDbContext Create(DbContextOptions<RetentionDbContext> options, ICurrentUser currentUser)
        => new(options);
}
