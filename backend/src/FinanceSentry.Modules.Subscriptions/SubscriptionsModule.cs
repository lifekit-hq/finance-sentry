namespace FinanceSentry.Modules.Subscriptions;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Application.Services;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FinanceSentry.Infrastructure.Persistence;

public static class SubscriptionsModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddSubscriptionsModule(config);
    }

    public static IServiceCollection AddSubscriptionsModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddUpdatedAtStamping();
        services.AddDbContext<SubscriptionsDbContext>(
            (sp, o) => o.UseNpgsql(config.GetConnectionString("Default")!, b => b.MigrationsHistoryTable("__EFMigrationsHistory", "public")).UseUpdatedAtStamping(sp));

        services.AddScoped<IDetectedSubscriptionRepository, DetectedSubscriptionRepository>();
        services.AddScoped<ISubscriptionDetectionResultService, SubscriptionDetectionResultService>();
        services.AddScoped<IActiveSubscriptionsReader, ActiveSubscriptionsReader>();
        services.AddScoped<ISubscriptionHygieneSummaryReader, SubscriptionHygieneSummaryReader>();

        return services;
    }
}
