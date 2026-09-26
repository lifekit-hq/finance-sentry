namespace FinanceSentry.Modules.Alerts;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain.Repositories;
using FinanceSentry.Modules.Alerts.Infrastructure.Jobs;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence.Repositories;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class AlertsModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddAlertsModule(config);
    }

    private sealed class JobRegistrar : IJobRegistrar
    {
        public void RegisterJobs(IServiceProvider sp)
        {
            sp.GetRequiredService<IRecurringJobManager>()
                .AddOrUpdate<AlertPurgeJob>("alert-purge", job => job.ExecuteAsync(CancellationToken.None), Cron.Monthly());
            sp.GetRequiredService<IRecurringJobManager>()
                .AddOrUpdate<AlertExpiryJob>("alert-expiry", job => job.ExecuteAsync(CancellationToken.None), Cron.Daily());
        }
    }

    public static IServiceCollection AddAlertsModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AlertsDbContext>(
            o => o.UseNpgsql(config.GetConnectionString("Default")!, b => b.MigrationsHistoryTable("__EFMigrationsHistory", "public")));

        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<IAlertGeneratorService, AlertGeneratorService>();
        services.AddScoped<IMaterialAlertReader, Infrastructure.Persistence.MaterialAlertReader>();
        services.AddScoped<AlertPurgeJob>();
        services.AddScoped<AlertExpiryJob>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IJobRegistrar, JobRegistrar>();

        return services;
    }
}
