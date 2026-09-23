namespace FinanceSentry.API.Migrations;

/// <summary>
/// What <see cref="MigrationExtensions.MigrateAllModules"/> did for this process: either every module
/// migrated, or the database was unreachable at startup and migrations were skipped for the contexts
/// listed here. An unreachable database is deliberately not fatal (see <c>MigrateContext</c>), which
/// leaves a gap once it comes back: the API is then serving against whatever schema the database
/// actually has, and a schema that is behind fails requests with errors that say nothing about
/// migrations. The <c>migrations</c> readiness check reads this so that gap is named where an operator
/// looks first.
/// </summary>
public sealed class StartupMigrationStatus
{
    private readonly List<Type> _skippedContexts = [];

    /// <summary>The DbContext types whose migrations were skipped because the database was unreachable.</summary>
    public IReadOnlyList<Type> SkippedContexts => _skippedContexts;

    /// <summary>True when at least one module's migrations were skipped at startup.</summary>
    public bool MigrationsSkipped => _skippedContexts.Count > 0;

    public void RecordSkipped(Type dbContextType)
    {
        if (!_skippedContexts.Contains(dbContextType))
            _skippedContexts.Add(dbContextType);
    }
}
