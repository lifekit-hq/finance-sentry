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
/// to be traced back from an unrelated failing request. Reachable again with nothing pending is Healthy:
/// the schema is current, no request fails, and the startup log already records the skip.
/// </summary>
public sealed class StartupMigrationsHealthCheck(
    StartupMigrationStatus status, IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public const string Name = "migrations";

    private const string SkippedAtStartup =
        "Module migrations were skipped at startup because the database was unreachable, so the API is " +
        "running against whatever schema the database has. Restart the API once the database is " +
        "reachable so the skipped migrations run.";

    private const string StillUnreachable = " Database still unreachable.";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!status.MigrationsSkipped)
            return HealthCheckResult.Healthy("All module migrations were applied at startup.");

        var pending = new List<string>();

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
                return HealthCheckResult.Unhealthy(SkippedAtStartup + StillUnreachable);
            }
        }

        if (pending.Count == 0)
            return HealthCheckResult.Healthy("Module migrations were skipped at startup, but the schema is up to date.");

        var description = new StringBuilder(SkippedAtStartup)
            .Append(" Schema is behind — pending migrations: ").Append(string.Join("; ", pending)).Append('.');
        return HealthCheckResult.Unhealthy(description.ToString());
    }
}
