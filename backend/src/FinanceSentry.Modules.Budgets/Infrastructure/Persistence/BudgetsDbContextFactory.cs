namespace FinanceSentry.Modules.Budgets.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

public class BudgetsDbContextFactory : IDesignTimeDbContextFactory<BudgetsDbContext>
{
    public BudgetsDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<BudgetsDbContext>();
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found in configuration");

        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlBuilder =>
            {
                npgsqlBuilder.MigrationsHistoryTable("__ef_migrations_history_budgets", "public");
            });

        return new BudgetsDbContext(optionsBuilder.Options, NoCurrentUser.Instance);
    }
}
