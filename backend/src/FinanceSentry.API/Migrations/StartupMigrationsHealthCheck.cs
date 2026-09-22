namespace FinanceSentry.API.Migrations;

using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Readiness check named <c>migrations</c>: reports whether this process applied its module migrations
/// at startup. The <c>database</c> check answers "is the database reachable now?"; this one answers the
/// question that check cannot — "did this process ever migrate the schema it is serving on?". When the
/// database was unreachable at startup and later returns, <c>database</c> goes Healthy while the schema
/// may still be behind; this check stays Unhealthy, names the pending migrations per module, and says
/// what to do (restart the API), so the cause is visible at the readiness endpoint rather than having
/// to be traced back from an unrelated failing request.
/// </summary>
public sealed class StartupMigrationsHealthCheck(
    StartupMigrationStatus status, IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public const string Name = "migrations";

    private const string SkippedAtStartup =
        "Module migrations were skipped at startup because the database was unreachable, so the API is " +
        "running against whatever schema the database has. Restart the API once the database is " +
        "reachable so the skipped migrations run.";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!status.MigrationsSkipped)
            return HealthCheckResult.Healthy("All module migrations were applied at startup.");

        var pending = new List<string>();
        var stillUnreachable = new List<string>();

        using var scope = scopeFactory.CreateScope();
        foreach (var contextType in status.SkippedContexts)
        {
            var dbContext = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            try
            {
                var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (pendingMigrations.Count > 0)
                    pending.Add($"{contextType.Name} ({string.Join(", ", pendingMigrations)})");
            }
            catch (Exception)
            {
                stillUnreachable.Add(contextType.Name);
            }
        }

        var description = new StringBuilder(SkippedAtStartup);
        if (stillUnreachable.Count > 0)
            description.Append(" Database still unreachable for: ").Append(string.Join(", ", stillUnreachable)).Append('.');
        if (pending.Count > 0)
            description.Append(" Schema is behind — pending migrations: ").Append(string.Join("; ", pending)).Append('.');

        // Reachable again with nothing pending: the schema happens to be current, so requests will work,
        // but this process never verified that itself — worth a restart, not worth failing readiness.
        if (stillUnreachable.Count == 0 && pending.Count == 0)
            return HealthCheckResult.Degraded(description.Append(" The schema is currently up to date.").ToString());

        return HealthCheckResult.Unhealthy(description.ToString());
    }
}
