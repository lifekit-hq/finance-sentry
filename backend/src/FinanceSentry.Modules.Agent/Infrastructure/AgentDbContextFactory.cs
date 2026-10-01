namespace FinanceSentry.Modules.Agent.Infrastructure;

using FinanceSentry.Core.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add</c> can build the model without the full host.
/// A connection string is only needed to select the provider at design time; a placeholder is used
/// when none is configured so the migration can be scaffolded offline in the sdk container.
/// </summary>
public class AgentDbContextFactory : IDesignTimeDbContextFactory<AgentDbContext>
{
    public AgentDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? "Host=localhost;Database=finance_sentry;Username=finance_user;Password=finance_password";

        var optionsBuilder = new DbContextOptionsBuilder<AgentDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            b => b.MigrationsHistoryTable("__ef_migrations_history_agent", "public"));

        return new AgentDbContext(optionsBuilder.Options, NoCurrentUser.Instance);
    }
}
