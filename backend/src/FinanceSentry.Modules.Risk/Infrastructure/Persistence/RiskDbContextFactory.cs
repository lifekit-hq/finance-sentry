namespace FinanceSentry.Modules.Risk.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class RiskDbContextFactory() : DesignTimeDbContextFactoryBase<RiskDbContext>("__ef_migrations_history_risk")
{
    protected override RiskDbContext Create(DbContextOptions<RiskDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
