namespace FinanceSentry.Modules.Budgets;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Budgets.Application.Services;
using FinanceSentry.Modules.Budgets.Domain.Repositories;
using FinanceSentry.Modules.Budgets.Infrastructure.Jobs;
using FinanceSentry.Modules.Budgets.Infrastructure.Persistence;
using FinanceSentry.Modules.Budgets.Infrastructure.Persistence.Repositories;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class BudgetsModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddBudgetsModule(config);
    }

    private sealed class JobRegistrar : IJobRegistrar
    {
        public void RegisterJobs(IServiceProvider sp)
        {
            var mgr = sp.GetRequiredService<IRecurringJobManager>();

            // Daily like BankSync's hygiene sentinels, but at 23:55 UTC rather than 00:00: the job
            // evaluates only its slot's month, so its last run for a month must see that month's final
            // day of spending (the bank sync scheduler runs every 10 minutes). Relaxed misfire
            // handling fires a missed occurrence once after downtime; the job itself skips it when
            // that is more than MaxStartDelay past the slot.
            var slot = BudgetBreachDetectionJob.ScheduledSlotUtc;
            mgr.AddOrUpdate<BudgetBreachDetectionJob>(
                "budget-breach-detection",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily(slot.Hour, slot.Minute),
                new RecurringJobOptions { MisfireHandling = MisfireHandlingMode.Relaxed });
        }
    }

    public static IServiceCollection AddBudgetsModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<BudgetsDbContext>(
            o => o.UseNpgsql(config.GetConnectionString("Default")!, b => b.MigrationsHistoryTable("__EFMigrationsHistory", "public")));

        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<ICategoryNormalizationService, CategoryNormalizationService>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<BudgetBreachDetectionJob>();
        services.AddSingleton<IJobRegistrar, JobRegistrar>();

        return services;
    }
}
