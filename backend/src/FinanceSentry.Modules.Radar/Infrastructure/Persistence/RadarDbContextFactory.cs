namespace FinanceSentry.Modules.Radar.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class RadarDbContextFactory() : DesignTimeDbContextFactoryBase<RadarDbContext>("__ef_migrations_history_radar")
{
    protected override RadarDbContext Create(DbContextOptions<RadarDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
