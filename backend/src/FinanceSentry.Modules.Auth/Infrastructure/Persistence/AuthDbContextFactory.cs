using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Auth.Infrastructure.Persistence;

public class AuthDbContextFactory() : DesignTimeDbContextFactoryBase<AuthDbContext>("__EFMigrationsHistory")
{
    protected override AuthDbContext Create(DbContextOptions<AuthDbContext> options, ICurrentUser currentUser)
        => new(options, currentUser);
}
