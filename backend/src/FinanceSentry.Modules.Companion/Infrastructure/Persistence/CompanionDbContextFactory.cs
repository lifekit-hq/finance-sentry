namespace FinanceSentry.Modules.Companion.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class CompanionDbContextFactory() : DesignTimeDbContextFactoryBase<CompanionDbContext>("__ef_migrations_history_companion")
{
    protected override CompanionDbContext Create(DbContextOptions<CompanionDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
