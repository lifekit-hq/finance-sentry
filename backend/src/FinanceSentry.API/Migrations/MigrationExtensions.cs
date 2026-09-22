namespace FinanceSentry.API.Migrations;

using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Categorization;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using FinanceSentry.Modules.Budgets.Infrastructure.Persistence;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Modules.Risk.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.Analytics.Infrastructure.Persistence;
using FinanceSentry.Modules.Retention.Infrastructure.Persistence;
using FinanceSentry.Modules.Agent.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

public static class MigrationExtensions
{
    // Research M012 (reconcile-and-drop the position cap) writes into risk.risk_rule_sets, and Risk
    // M002 (reconcile-and-drop the allocation targets) writes into research.investment_policy_statements
    // — a genuine data dependency in both directions between the two modules (#661). Neither module can
    // simply migrate wholesale before the other: Research needs Risk's table (created by Risk M001)
    // before M012 runs, and Risk needs Research's table (created by Research M003, long before M012) for
    // its own M002. So Research migrates up to the migration before M012, Risk migrates in full (M002
    // finds research.investment_policy_statements already there from Research M003), and only then does
    // Research finish from M012 onward (finds risk.risk_rule_sets already there from Risk M001).
    // The partial step is skipped once M011 is applied: Migrate(target) moves the database to exactly
    // that migration, so on an already-migrated database it would roll back M012+ (and their data).
    private const string ResearchMigrationBeforeRiskDependency = "20260806140817_M011_CleanAnalystActionFirms";

    public static WebApplication MigrateAllModules(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        MigrateContext<AuthDbContext>(sp, app.Logger);
        MigrateContext<BankSyncDbContext>(sp, app.Logger);
        MigrateContext<CryptoSyncDbContext>(sp, app.Logger);
        MigrateContext<BrokerageSyncDbContext>(sp, app.Logger);
        MigrateContext<AlertsDbContext>(sp, app.Logger);
        MigrateContext<BudgetsDbContext>(sp, app.Logger);
        MigrateContext<SubscriptionsDbContext>(sp, app.Logger);
        MigrateContext<WealthDbContext>(sp, app.Logger);
        MigrateContext<ResearchDbContext>(sp, app.Logger, targetMigration: ResearchMigrationBeforeRiskDependency);
        MigrateContext<RiskDbContext>(sp, app.Logger);
        MigrateContext<ResearchDbContext>(sp, app.Logger);
        MigrateContext<RadarDbContext>(sp, app.Logger);
        MigrateContext<CompanionDbContext>(sp, app.Logger);
        MigrateContext<AnalyticsDbContext>(sp, app.Logger);
        MigrateContext<RetentionDbContext>(sp, app.Logger);
        MigrateContext<AgentDbContext>(sp, app.Logger);

        SeedBankSyncCategories(sp, app.Logger);

        return app;
    }

    private static void SeedBankSyncCategories(IServiceProvider sp, ILogger logger)
    {
        try
        {
            CategorySeeder.Seed(sp.GetRequiredService<BankSyncDbContext>());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Category reference seeding failed. Startup will continue.");
        }
    }

    // A failed migration is left half-applied (EF Core wraps each migration in its own transaction, so
    // everything before it committed) — a half-migrated schema that the API then serves on is exactly
    // the silent failure #661 cost us (Research's M012 threw for a missing risk.risk_rule_sets, the API
    // started anyway, and the first anyone knew was an unrelated 500 on saving a thesis). So a migration
    // failure against a reachable database is no longer caught here: it is logged with enough to name
    // the module, the migration, and the fix, then left to propagate out of MigrateAllModules and abort
    // startup before app.Run() — loud and immediate rather than a silent partial schema.
    //
    // This is deliberately narrower than "any exception here stops startup": a database that cannot be
    // reached at all (down, still starting, or — in tests — pointed at a connection string that is
    // unreachable on purpose to exercise the /api/v1/health/ready "database" check, see
    // ObservabilityApiFactory) is a different failure the app already has a contract for (SC-003
    // readiness). CanConnect() tells the two apart: only a migration that fails while the database is
    // reachable is treated as the #661 half-migrated-schema hazard.
    private static void MigrateContext<TContext>(IServiceProvider sp, ILogger logger, string? targetMigration = null)
        where TContext : DbContext
    {
        var context = sp.GetRequiredService<TContext>();

        // Test hosts swap individual contexts onto EF Core's InMemory provider (e.g.
        // BankSyncApiFactory.ReplaceDbContextWithInMemory), which has no migrations at all — relational
        // APIs like GetMigrations()/GetAppliedMigrations() throw for it by design. Nothing to migrate
        // there, so skip rather than treat "not a relational database" as a migration failure.
        if (!context.Database.IsRelational())
            return;

        if (!context.Database.CanConnect())
        {
            logger.LogError(
                "Cannot reach the database for {Context}; skipping its migration. If this is unexpected, " +
                "check /api/v1/health/ready — the database check there reports connectivity independently.",
                typeof(TContext).Name);
            return;
        }

        if (targetMigration is not null && context.Database.GetAppliedMigrations().Contains(targetMigration))
            return;

        try
        {
            context.GetService<IMigrator>().Migrate(targetMigration);
        }
        catch (Exception ex)
        {
            var failedMigration = context.Database.GetMigrations()
                .Except(context.Database.GetAppliedMigrations())
                .FirstOrDefault() ?? "(unknown)";

            var startupException = new StartupMigrationException(typeof(TContext).Name, failedMigration, ex);
            logger.LogCritical(startupException, "Startup migration failed — the API will not start.");
            throw startupException;
        }
    }
}
