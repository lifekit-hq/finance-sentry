namespace FinanceSentry.Modules.CryptoSync;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Jobs;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.Repositories;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Encryption;

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
            sp.GetRequiredService<IRecurringJobManager>()
                .AddOrUpdate<BinanceSyncJob>("binance-sync", job => job.ExecuteAsync(), "*/15 * * * *");
        }
    }

    public static IServiceCollection AddCryptoSyncModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<CryptoSyncDbContext>(
            o => o.UseNpgsql(config.GetConnectionString("Default")!, b => b.MigrationsHistoryTable("__EFMigrationsHistory", "public")));

        services.AddHttpClient<BinanceHttpClient>();
        services.AddSingleton<BinanceHoldingsAggregator>();
        services.AddSingleton<CostBasisCalculator>();
        services.AddScoped<ICryptoExchangeAdapter, BinanceAdapter>();
        services.AddScoped<IBinanceCredentialRepository, BinanceCredentialRepository>();
        // #493: this module's credential store joins key rotation.
        services.AddScoped<ICredentialRotationTarget, BinanceCredentialRotationTarget>();
        services.AddScoped<ICryptoHoldingRepository, CryptoHoldingRepository>();
        services.AddScoped<ICryptoHoldingsReader, CryptoHoldingsReader>();
        services.AddScoped<BinanceSyncJob>();

        services.AddSingleton<IJobRegistrar, JobRegistrar>();

        return services;
    }
}
