namespace FinanceSentry.API.Migrations;

/// <summary>
/// Thrown by <see cref="MigrationExtensions.MigrateAllModules"/> when a module's EF Core migration
/// fails during startup. Previously this was caught and logged while the API kept starting, so the
/// process could come up with a module's schema stuck part-way through a migration — the first sign
/// was an unrelated 500 with no mention of a migration (#661/#664). Startup now fails loudly instead:
/// this exception is left unhandled, so the process exits non-zero and never reaches <c>app.Run()</c>,
/// rather than serving on a half-migrated schema.
/// </summary>
public sealed class StartupMigrationException : Exception
{
    public StartupMigrationException(string dbContextName, string failedMigration, Exception innerException)
        : base(
            $"Startup migration failed: {dbContextName} could not apply migration '{failedMigration}'. " +
            "The API will not start — serving on a half-migrated schema is worse than not serving at all. " +
            "Fix the underlying error (see the inner exception) and restart the API; migrations are " +
            "idempotent, so the restart resumes from this migration rather than re-running earlier ones. " +
            $"Underlying error: {innerException.Message}",
            innerException)
    {
    }
}
