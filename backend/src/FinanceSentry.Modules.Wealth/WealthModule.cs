namespace FinanceSentry.Modules.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FinanceSentry.Modules.Wealth.Domain.Services;
using FinanceSentry.Modules.Wealth.Infrastructure.Jobs;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence.Repositories;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class WealthModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddWealthModule(config);
    }

    /// <summary>
    /// Worker role only (issue #613): the startup catch-up is background work, and it ran in the MCP
    /// host beside the API for as long as the module registrar carried it.
    /// </summary>
    private sealed class WorkerRegistrar : IWorkerRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddHostedService<NetWorthSnapshotCatchUpHostedService>();
    }

    private sealed class JobRegistrar : IJobRegistrar
    {
        public void RegisterJobs(IServiceProvider sp)
        {
            var mgr = sp.GetRequiredService<IRecurringJobManager>();
            mgr.AddOrUpdate<NetWorthSnapshotJob>(
                "net-worth-snapshot",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily(1));
        }
    }

    public static IServiceCollection AddWealthModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<WealthDbContext>(
            o => o.UseNpgsql(config.GetConnectionString("Default")!,
                b => b.MigrationsHistoryTable("__ef_migrations_history_wealth", "public")));

        services.AddScoped<INetWorthSnapshotRepository, NetWorthSnapshotRepository>();
        services.AddScoped<INetWorthSnapshotService, NetWorthSnapshotService>();
        services.AddScoped<NetWorthSnapshotBackfillService>();
        services.AddScoped<INetWorthSnapshotJobScheduler, NetWorthSnapshotJobScheduler>();
        services.AddScoped<IWealthAggregationService, WealthAggregationService>();

        services.AddScoped<NetWorthSnapshotJob>();

        services.AddSingleton<IJobRegistrar, JobRegistrar>();

        return services;
    }
}
