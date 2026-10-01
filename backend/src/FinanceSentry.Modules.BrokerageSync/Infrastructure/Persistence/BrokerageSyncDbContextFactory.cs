using FinanceSentry.Core.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;

public sealed class BrokerageSyncDbContextFactory : IDesignTimeDbContextFactory<BrokerageSyncDbContext>
{
    public BrokerageSyncDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found in configuration");

        var optionsBuilder = new DbContextOptionsBuilder<BrokerageSyncDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlBuilder =>
            {
                npgsqlBuilder.MigrationsHistoryTable("__ef_migrations_history", "public");
            });

        return new BrokerageSyncDbContext(optionsBuilder.Options, NoCurrentUser.Instance);
    }
}
