namespace FinanceSentry.Modules.Analytics.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class AnalyticsDbContextFactory() : DesignTimeDbContextFactoryBase<AnalyticsDbContext>("__ef_migrations_history_analytics")
{
    protected override AnalyticsDbContext Create(DbContextOptions<AnalyticsDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
