namespace FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class SubscriptionsDbContextFactory() : DesignTimeDbContextFactoryBase<SubscriptionsDbContext>("__ef_migrations_history_subscriptions")
{
    protected override SubscriptionsDbContext Create(DbContextOptions<SubscriptionsDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
