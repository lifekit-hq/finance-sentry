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
using FinanceSentry.Modules.Events.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

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
        var anyContextMigrated = false;

        MigrateContext<AuthDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<BankSyncDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<CryptoSyncDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<BrokerageSyncDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<AlertsDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<BudgetsDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<SubscriptionsDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<WealthDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<ResearchDbContext>(sp, app.Logger, ref anyContextMigrated, targetMigration: ResearchMigrationBeforeRiskDependency);
        MigrateContext<RiskDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<ResearchDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<RadarDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<CompanionDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<AnalyticsDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<RetentionDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<AgentDbContext>(sp, app.Logger, ref anyContextMigrated);
        MigrateContext<EventsDbContext>(sp, app.Logger, ref anyContextMigrated);

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
    // started anyway, and the first anyone knew was an unrelated 500 on saving a thesis). Three outcomes:
    // (a) A failed migration against a reachable database stops startup.
    // (b) A half-migrated startup caused by losing the database connection partway through also stops startup.
    // (c) A database that is simply not reachable yet (nothing has migrated against it so far) is unchanged
    //     and does not stop startup — /api/v1/health/ready reports it, and test hosts such as
    //     ObservabilityApiFactory point contexts at a deliberately unreachable database and rely on it.
    // A reachable server whose database does not exist yet counts as reachable: Migrate() creates it.
    private static void MigrateContext<TContext>(
        IServiceProvider sp, ILogger logger, ref bool anyContextMigrated, string? targetMigration = null)
        where TContext : DbContext
    {
        var context = sp.GetRequiredService<TContext>();
        var contextName = typeof(TContext).Name;

        // Test hosts swap individual contexts onto EF Core's InMemory provider (e.g.
        // BankSyncApiFactory.ReplaceDbContextWithInMemory), which has no migrations at all — relational
        // APIs like GetMigrations()/GetAppliedMigrations() throw for it by design. Nothing to migrate
        // there, so skip rather than treat "not a relational database" as a migration failure.
        if (!context.Database.IsRelational())
            return;

        var connectivityError = ProbeConnectivity(context.Database);
        if (connectivityError is not null)
        {
            if (anyContextMigrated)
            {
                const string migration = "(not attempted — database connection lost after earlier modules migrated)";
                var halfMigrated = new StartupMigrationException(contextName, migration, connectivityError);
                logger.LogCritical(
                    halfMigrated,
                    "STARTUP MIGRATION FAILURE: {Context} could not apply migration {Migration} — the API will not start.",
                    contextName, migration);
                throw halfMigrated;
            }

            logger.LogError(
                connectivityError,
                "Cannot reach the database for {Context}; skipping its migration. If this is unexpected, " +
                "check /api/v1/health/ready — the database check there reports connectivity independently.",
                contextName);
            return;
        }

        if (targetMigration is not null && context.Database.GetAppliedMigrations().Contains(targetMigration))
        {
            anyContextMigrated = true;
            return;
        }

        try
        {
            context.GetService<IMigrator>().Migrate(targetMigration);
            anyContextMigrated = true;
        }
        catch (Exception ex)
        {
            var failedMigration = FindFailedMigration(context.Database);
            var startupException = new StartupMigrationException(contextName, failedMigration, ex);
            logger.LogCritical(
                startupException,
                "STARTUP MIGRATION FAILURE: {Context} could not apply migration {Migration} — the API will not start.",
                contextName, failedMigration);
            throw startupException;
        }
    }

    private static Exception? ProbeConnectivity(DatabaseFacade database)
    {
        try
        {
            using var connection = new NpgsqlConnection(database.GetConnectionString());
            connection.Open();
            return null;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
        {
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static string FindFailedMigration(DatabaseFacade database)
    {
        try
        {
            return database.GetMigrations().Except(database.GetAppliedMigrations()).FirstOrDefault() ?? "(unknown)";
        }
        catch (Exception)
        {
            return "(unknown)";
        }
    }
}
