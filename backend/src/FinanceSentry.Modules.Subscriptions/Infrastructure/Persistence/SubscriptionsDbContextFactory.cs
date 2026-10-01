namespace FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

public class SubscriptionsDbContextFactory : IDesignTimeDbContextFactory<SubscriptionsDbContext>
{
    public SubscriptionsDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<SubscriptionsDbContext>();
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found in configuration");

        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlBuilder =>
            {
                npgsqlBuilder.MigrationsHistoryTable("__ef_migrations_history_subscriptions", "public");
            });

        return new SubscriptionsDbContext(optionsBuilder.Options, NoCurrentUser.Instance);
    }
}
