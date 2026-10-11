namespace FinanceSentry.Modules.Wealth.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class WealthDbContextFactory() : DesignTimeDbContextFactoryBase<WealthDbContext>(WealthModule.MigrationsHistoryTable)
{
    protected override WealthDbContext Create(DbContextOptions<WealthDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
