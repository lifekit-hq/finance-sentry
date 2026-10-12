namespace FinanceSentry.Modules.Alerts.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class AlertsDbContextFactory() : DesignTimeDbContextFactoryBase<AlertsDbContext>("__ef_migrations_history_alerts")
{
    protected override AlertsDbContext Create(DbContextOptions<AlertsDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
