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

    private static void MigrateContext<TContext>(IServiceProvider sp, ILogger logger, string? targetMigration = null)
        where TContext : DbContext
    {
        try
        {
            sp.GetRequiredService<TContext>().GetService<IMigrator>().Migrate(targetMigration);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Migration failed for {Context}. Startup will continue.", typeof(TContext).Name);
        }
    }
}
