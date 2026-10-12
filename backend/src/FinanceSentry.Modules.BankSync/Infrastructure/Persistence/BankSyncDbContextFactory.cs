namespace FinanceSentry.Modules.BankSync.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class BankSyncDbContextFactory() : DesignTimeDbContextFactoryBase<BankSyncDbContext>("__ef_migrations_history")
{
    protected override BankSyncDbContext Create(DbContextOptions<BankSyncDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
