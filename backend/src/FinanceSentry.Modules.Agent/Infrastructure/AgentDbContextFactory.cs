namespace FinanceSentry.Modules.Agent.Infrastructure;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class AgentDbContextFactory() : DesignTimeDbContextFactoryBase<AgentDbContext>("__ef_migrations_history_agent")
{
    protected override AgentDbContext Create(DbContextOptions<AgentDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
