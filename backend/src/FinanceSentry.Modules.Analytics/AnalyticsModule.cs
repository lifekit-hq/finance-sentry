namespace FinanceSentry.Modules.Analytics;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Analytics.Application.Services;
using FinanceSentry.Modules.Analytics.Domain.Repositories;
using FinanceSentry.Modules.Analytics.Infrastructure.Persistence;
using FinanceSentry.Modules.Analytics.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FinanceSentry.Infrastructure.Persistence;

public static class AnalyticsModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddAnalyticsModule(config);
    }

    public static IServiceCollection AddAnalyticsModule(
        this IServiceCollection services, IConfiguration config)
    {
        // Writable context: owns query_audit + the migration home (views/role/RLS created via raw SQL).
        services.AddUpdatedAtStamping();
        services.AddDbContext<AnalyticsDbContext>(
            (sp, o) => o.UseNpgsql(
                config.GetConnectionString("Default")!,
                b => b.MigrationsHistoryTable("__ef_migrations_history_analytics", "public")).UseUpdatedAtStamping(sp));

        services.Configure<AnalyticsOptions>(config.GetSection(AnalyticsOptions.SectionName));

        // The read-only path connects as fs_readonly. Falls back to Default only if ReadOnly is unset
        // (dev/single-node); production MUST provision a distinct SELECT-only connection (T023).
        var readOnlyConnectionString =
            config.GetConnectionString("ReadOnly") ?? config.GetConnectionString("Default")!;
        services.PostConfigure<AnalyticsOptions>(o => o.ReadOnlyConnectionString = readOnlyConnectionString);

        services.AddScoped<IQueryAuditRepository, QueryAuditRepository>();

        services.AddSingleton<ISqlGuard, SqlGuard>();
        services.AddSingleton<ICuratedSchema, CuratedSchema>();
        services.AddScoped<IReadOnlyQueryExecutor, ReadOnlyQueryExecutor>();

        return services;
    }
}
