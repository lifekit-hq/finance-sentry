namespace FinanceSentry.Modules.CryptoSync;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Encryption;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Jobs;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class CryptoSyncModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddCryptoSyncModule(config);
    }

    private sealed class JobRegistrar : IJobRegistrar
    {
        public void RegisterJobs(IServiceProvider sp)
        {
            var jobs = sp.GetRequiredService<IRecurringJobManager>();
            jobs.AddOrUpdate<BinanceSyncJob>("binance-sync", job => job.ExecuteAsync(), "*/15 * * * *");
            jobs.AddOrUpdate<RevolutXSyncJob>("revolut-x-sync", job => job.ExecuteAsync(), "*/15 * * * *");
        }
    }

    public static IServiceCollection AddCryptoSyncModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<CryptoSyncDbContext>(
            o => o.UseNpgsql(config.GetConnectionString("Default")!, b => b.MigrationsHistoryTable("__EFMigrationsHistory", "public")));

        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient<BinanceHttpClient>();
        services.AddSingleton<BinanceHoldingsAggregator>();
        services.AddScoped<ICryptoExchangeAdapter, BinanceAdapter>();

        services.AddHttpClient<RevolutXHttpClient>();
        services.AddSingleton<RevolutXHoldingsAggregator>();
        services.AddScoped<ICryptoExchangeAdapter, RevolutXAdapter>();

        services.AddScoped<CryptoExchangeAdapterRegistry>();
        services.AddSingleton<CostBasisCalculator>();
        services.AddScoped<IExchangeCredentialRepository, ExchangeCredentialRepository>();
        // #493: this module's credential store joins key rotation.
        services.AddScoped<ICredentialRotationTarget, ExchangeCredentialRotationTarget>();
        services.AddScoped<ICryptoHoldingRepository, CryptoHoldingRepository>();
        services.AddScoped<ICryptoHoldingsReader, CryptoHoldingsReader>();
        services.AddScoped<BinanceSyncJob>();
        services.AddScoped<RevolutXSyncJob>();

        services.AddSingleton<IJobRegistrar, JobRegistrar>();

        return services;
    }
}
