namespace FinanceSentry.Modules.Subscriptions;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Application.Icons;
using FinanceSentry.Modules.Subscriptions.Application.Services;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Icons;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.AddScoped<IUserDataEraser, SubscriptionsUserDataEraser>();
        services.AddScoped<IUserDataExporter, SubscriptionsUserDataExporter>();

        services.AddScoped<IDetectedSubscriptionRepository, DetectedSubscriptionRepository>();
        services.AddScoped<ISubscriptionDetectionResultService, SubscriptionDetectionResultService>();
        services.AddScoped<IActiveSubscriptionsReader, ActiveSubscriptionsReader>();

        services.AddSingleton<IMerchantBrandCatalog, MerchantBrandCatalog>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IMerchantIconService, MerchantIconService>();
        services.AddHttpClient<IMerchantIconFetcher, MerchantIconFetcher>(client => client.Timeout = MerchantIconFetcher.RequestTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Redirects are followed by hand, same host only (see MerchantIconFetcher).
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(3),
                ConnectCallback = PublicAddressGuard.ConnectAsync,
                UseCookies = false,
            });

        return services;
    }
}
