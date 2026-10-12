namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public sealed class CryptoSyncDbContextFactory() : DesignTimeDbContextFactoryBase<CryptoSyncDbContext>("__ef_migrations_history")
{
    protected override CryptoSyncDbContext Create(DbContextOptions<CryptoSyncDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
