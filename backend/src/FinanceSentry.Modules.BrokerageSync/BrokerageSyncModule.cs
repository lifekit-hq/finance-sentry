namespace FinanceSentry.Modules.BrokerageSync;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Application.Connect;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.OAuth;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence.Repositories;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Encryption;

public static class BrokerageSyncModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddBrokerageSyncModule(config);
    }

    private sealed class JobRegistrar : IJobRegistrar
    {
        public void RegisterJobs(IServiceProvider sp)
        {
            var mgr = sp.GetRequiredService<IRecurringJobManager>();
            mgr.AddOrUpdate<IBKRSyncJob>("ibkr-sync", job => job.ExecuteAsync(), "*/15 * * * *");
            // The IBeam health-check job (per-user container reconcile) is
            // obsolete under OAuth — remove any previously-scheduled instance.
            mgr.RemoveIfExists("ibeam-health-check");
        }
    }

    public static IServiceCollection AddBrokerageSyncModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<BrokerageSyncDbContext>(
            o => o.UseNpgsql(config.GetConnectionString("Default")!, b => b.MigrationsHistoryTable("__EFMigrationsHistory", "public")));

        // Blocking connect: request-scoped, awaited by the controller. No
        // session store, no polling — the HTTP request itself is the state
        // machine, and client disconnect cancels the whole pipeline via the
        // request CancellationToken (which triggers rollback in the connector).
        services.AddScoped<IIBKRConnector, IBKRConnector>();

        // IBKR access now runs over OAuth 1.0a: no per-user container, no
        // password, no 2FA. Each user's stored keys are resolved and used to
        // sign requests directly against IBKR's Web API.
        services.Configure<IbkrOAuthOptions>(config.GetSection(IbkrOAuthOptions.SectionName));
        services.AddScoped<IIbkrCredentialResolver, IbkrCredentialResolver>();
        services.AddHttpClient<IbkrOAuthClient>();
        services.AddScoped<IBrokerAdapter, IbkrOAuthAdapter>();

        services.AddScoped<IIBKRCredentialRepository, IBKRCredentialRepository>();
        // #493: this module's credential store joins key rotation.
        services.AddScoped<ICredentialRotationTarget, IBKRCredentialRotationTarget>();
        services.AddScoped<IBrokerageHoldingRepository, BrokerageHoldingRepository>();
        services.AddScoped<IBrokerageHoldingsReader, BrokerageHoldingsReader>();
        services.AddScoped<IBKRSyncJob>();

        services.AddSingleton<IJobRegistrar, JobRegistrar>();

        return services;
    }
}
