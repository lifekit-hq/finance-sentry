namespace FinanceSentry.Modules.Companion.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

public class CompanionDbContextFactory : IDesignTimeDbContextFactory<CompanionDbContext>
{
    public CompanionDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found in configuration");

        var optionsBuilder = new DbContextOptionsBuilder<CompanionDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            b => b.MigrationsHistoryTable("__ef_migrations_history_companion", "public"));

        return new CompanionDbContext(optionsBuilder.Options, NoCurrentUser.Instance);
    }
}
