namespace FinanceSentry.Modules.BankSync.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

/// <summary>
/// DbContext factory for EF Core migrations.
/// Required for 'dotnet ef migrations add' and 'dotnet ef database update' CLI commands.
/// </summary>
public class BankSyncDbContextFactory : IDesignTimeDbContextFactory<BankSyncDbContext>
{
    public BankSyncDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<BankSyncDbContext>();
        var connectionString = configuration.GetConnectionString("Default") 
            ?? throw new InvalidOperationException("Connection string 'Default' not found in configuration");

        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlBuilder =>
            {
                npgsqlBuilder.MigrationsHistoryTable("__ef_migrations_history", "public");
            });

        return new BankSyncDbContext(optionsBuilder.Options, NoCurrentUser.Instance);
    }
}
