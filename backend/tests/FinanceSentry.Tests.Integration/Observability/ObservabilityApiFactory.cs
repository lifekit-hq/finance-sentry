namespace FinanceSentry.Tests.Integration.Observability;

using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// WebApplicationFactory for the observability contract tests. Runs under the <c>Testing</c> environment
/// so Hangfire uses in-memory storage (no database required to boot); the <c>database</c> health check
/// still points at an unreachable connection string, which the readiness tests rely on to exercise the
/// unhealthy path. The fully-healthy readiness path is validated against the live stack in quickstart T021.
/// </summary>
public class ObservabilityApiFactory : WebApplicationFactory<Program>
{
    private static readonly InMemoryDatabaseRoot BankSyncDbRoot = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            ReplaceWithInMemory<BankSyncDbContext>(services, "observability-banksync", BankSyncDbRoot);
        });

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default",
            "Host=localhost;Port=5432;Database=unreachable;Username=test;Password=test;Timeout=1;Command Timeout=1");
        builder.UseSetting("Deduplication:MasterKeyBase64",
            "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
        builder.UseSetting("Encryption:CurrentKeyVersion", "1");
        builder.UseSetting("Encryption:Keys:1",
            "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
        builder.UseSetting("Jwt:Secret",
            "test-jwt-secret-key-for-integration-tests-minimum-32-chars");
        builder.UseSetting("GoogleOAuth:ClientId", "test-client-id");
    }

    private static void ReplaceWithInMemory<TContext>(
        IServiceCollection services,
        string dbName,
        InMemoryDatabaseRoot root)
        where TContext : DbContext
    {
        var toRemove = services
            .Where(d => d.ServiceType == typeof(DbContextOptions<TContext>)
                     || d.ServiceType == typeof(TContext)
                     || d.ServiceType == typeof(IDbContextOptionsConfiguration<TContext>))
            .ToList();
        foreach (var d in toRemove)
            services.Remove(d);

        services.AddDbContext<TContext>(options =>
            options.UseInMemoryDatabase(dbName, root));
    }
}
