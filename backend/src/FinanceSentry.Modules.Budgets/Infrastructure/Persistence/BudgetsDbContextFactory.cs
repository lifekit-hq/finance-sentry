namespace FinanceSentry.Modules.Budgets.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class BudgetsDbContextFactory() : DesignTimeDbContextFactoryBase<BudgetsDbContext>("__ef_migrations_history_budgets")
{
    protected override BudgetsDbContext Create(DbContextOptions<BudgetsDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
