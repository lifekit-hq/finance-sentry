namespace FinanceSentry.Modules.Wealth.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

public class WealthDbContextFactory : IDesignTimeDbContextFactory<WealthDbContext>
{
    public WealthDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found in configuration");

        var optionsBuilder = new DbContextOptionsBuilder<WealthDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            b => b.MigrationsHistoryTable("__ef_migrations_history_wealth", "public"));

        return new WealthDbContext(optionsBuilder.Options, NoCurrentUser.Instance);
    }
}
