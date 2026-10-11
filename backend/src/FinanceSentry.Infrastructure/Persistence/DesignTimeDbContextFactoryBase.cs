namespace FinanceSentry.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

/// <summary>
/// The <c>dotnet ef</c> factory every module repeated: connection string from configuration, the module's own
/// migrations history table in <c>public</c>, no person in scope.
/// </summary>
public abstract class DesignTimeDbContextFactoryBase<TContext>(string migrationsHistoryTable) : IDesignTimeDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found in configuration");

        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        optionsBuilder.UseNpgsql(connectionString, b => b.MigrationsHistoryTable(migrationsHistoryTable, "public"));

        return Create(optionsBuilder.Options, NoCurrentUser.Instance);
    }

    protected abstract TContext Create(DbContextOptions<TContext> options, ICurrentUser currentUser);
}
