namespace FinanceSentry.Modules.Research.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class ResearchDbContextFactory() : DesignTimeDbContextFactoryBase<ResearchDbContext>("__ef_migrations_history_research")
{
    protected override ResearchDbContext Create(DbContextOptions<ResearchDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
