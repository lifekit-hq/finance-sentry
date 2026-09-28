namespace FinanceSentry.Modules.Risk;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using FinanceSentry.Modules.Risk.Infrastructure.Jobs;
using FinanceSentry.Modules.Risk.Infrastructure.Persistence;
using FinanceSentry.Modules.Risk.Infrastructure.Persistence.Repositories;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public static class RiskModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddRiskModule(config);
    }

    private sealed class JobRegistrar : IJobRegistrar
    {
        public void RegisterJobs(IServiceProvider sp)
        {
            var mgr = sp.GetRequiredService<IRecurringJobManager>();
            var options = sp.GetRequiredService<IOptions<RiskOptions>>().Value;

            mgr.AddOrUpdate<RiskCheckJob>(
                "risk-check",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily(options.RiskCheckHourUtc));
        }
    }

    public static IServiceCollection AddRiskModule(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<RiskOptions>(config.GetSection(RiskOptions.SectionName));

        services.AddDbContext<RiskDbContext>(
            o => o.UseNpgsql(
                config.GetConnectionString("Default")!,
                b => b.MigrationsHistoryTable("__ef_migrations_history_risk", "public")));

        services.AddScoped<IRiskRuleSetRepository, RiskRuleSetRepository>();
        // #673: published read port - the Integration adapters reach the rule set only through it.
        services.AddScoped<IRiskLimitsReader, RiskLimitsReader>();
        services.AddScoped<IPolicyViolationAckRepository, PolicyViolationAckRepository>();
        services.AddScoped<IHoldingSnapshotRepository, HoldingSnapshotRepository>();

        services.AddScoped<IBookSnapshotReader, BookSnapshotReader>();
        services.AddScoped<IRiskEvaluationService, RiskEvaluationService>();
        services.AddScoped<ITurnoverTracker, TurnoverTracker>();
        services.AddScoped<IAddToBrokenThesisDetector, AddToBrokenThesisDetector>();
        services.AddScoped<IRiskPolicyGate, RiskPolicyGate>();

        services.AddScoped<RiskCheckJob>();

        services.AddSingleton<IJobRegistrar, JobRegistrar>();

        return services;
    }
}
