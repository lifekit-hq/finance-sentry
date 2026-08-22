namespace FinanceSentry.Modules.Liquidity;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Liquidity.Application.Jobs;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class LiquidityModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddLiquidityModule();
    }

    private sealed class JobRegistrar : IJobRegistrar
    {
        public void RegisterJobs(IServiceProvider sp)
        {
            var mgr = sp.GetRequiredService<IRecurringJobManager>();
            mgr.AddOrUpdate<LiquiditySentinelJob>(
                "liquidity-sentinel",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily());
        }
    }

    public static IServiceCollection AddLiquidityModule(this IServiceCollection services)
    {
        services.AddScoped<LiquiditySentinelJob>();
        services.AddSingleton<IJobRegistrar, JobRegistrar>();
        return services;
    }
}
