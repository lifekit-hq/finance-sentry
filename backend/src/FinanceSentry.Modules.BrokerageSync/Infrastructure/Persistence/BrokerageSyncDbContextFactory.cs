namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public sealed class BrokerageSyncDbContextFactory() : DesignTimeDbContextFactoryBase<BrokerageSyncDbContext>("__ef_migrations_history")
{
    protected override BrokerageSyncDbContext Create(DbContextOptions<BrokerageSyncDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
