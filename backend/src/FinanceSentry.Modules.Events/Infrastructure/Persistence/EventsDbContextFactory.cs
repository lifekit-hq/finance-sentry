namespace FinanceSentry.Modules.Events.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class EventsDbContextFactory() : DesignTimeDbContextFactoryBase<EventsDbContext>(EventsModule.MigrationsHistoryTable)
{
    protected override EventsDbContext Create(DbContextOptions<EventsDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
