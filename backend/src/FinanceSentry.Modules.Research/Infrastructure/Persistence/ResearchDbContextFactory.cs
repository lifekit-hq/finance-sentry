namespace FinanceSentry.Modules.Research.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

public class ResearchDbContextFactory : IDesignTimeDbContextFactory<ResearchDbContext>
{
    public ResearchDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found in configuration");

        var optionsBuilder = new DbContextOptionsBuilder<ResearchDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlBuilder =>
            {
                npgsqlBuilder.MigrationsHistoryTable("__ef_migrations_history_research", "public");
            });

        return new ResearchDbContext(optionsBuilder.Options, NoCurrentUser.Instance);
    }
}
