namespace FinanceSentry.Modules.BrokerageSync;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Connections;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.BrokerageSync.Application.Connect;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Domain.Ports;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.OAuth;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence.Repositories;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Encryption;
using FinanceSentry.Infrastructure.Persistence;

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

            mgr.AddOrUpdate<IbkrFlexIncrementalSyncJob>(
                "ibkr-flex-incremental-sync", job => job.ExecuteAsync(), "0 6 * * *");
            // One-shot full-history backfill: never fires on its own (Cron.Never()),
            // only triggered manually from the Hangfire dashboard.
            mgr.AddOrUpdate<IbkrFlexBackfillJob>(
                "ibkr-flex-backfill", job => job.ExecuteAsync(), Cron.Never());

            var inzhur = sp.GetRequiredService<IOptions<InzhurOptions>>().Value;
            mgr.AddOrUpdate<InzhurSyncJob>("inzhur-sync", job => job.ExecuteAsync(), inzhur.SyncCron);
        }
    }

    public static IServiceCollection AddBrokerageSyncModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddConnectionHealthShadow(config);
        services.AddUpdatedAtStamping();
        services.AddDbContext<BrokerageSyncDbContext>(
            (sp, o) => o.UseNpgsql(config.GetConnectionString("Default")!, b => b.MigrationsHistoryTable("__EFMigrationsHistory", "public")).UseUpdatedAtStamping(sp));

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

        // IBKR Flex Web Service: a second, separate broker credential (token +
        // Activity Flex Query id) for historical statement pulls. Additive to
        // the OAuth connection above — no shared state, no shared session.
        services.Configure<IbkrFlexOptions>(config.GetSection(IbkrFlexOptions.SectionName));
        services.AddScoped<IIbkrFlexConnector, IbkrFlexConnector>();
        services.AddScoped<IIbkrFlexCredentialResolver, IbkrFlexCredentialResolver>();
        services.AddSingleton<IbkrFlexRateLimiter>();
        services.AddHttpClient<IIbkrFlexClient, IbkrFlexClient>();
        services.AddScoped<IIbkrFlexStatementFetcher, IbkrFlexStatementFetcher>();
        services.AddScoped<IIBKRFlexCredentialRepository, IBKRFlexCredentialRepository>();
        services.AddScoped<ICredentialRotationTarget, IBKRFlexCredentialRotationTarget>();
        services.AddScoped<IBrokerageHoldingRepository, BrokerageHoldingRepository>();
        services.AddScoped<IBrokerageInstrumentRepository, BrokerageInstrumentRepository>();
        services.AddScoped<IBrokerageTradeRepository, BrokerageTradeRepository>();
        services.AddScoped<IBrokerageCashTransactionRepository, BrokerageCashTransactionRepository>();
        services.AddScoped<IIbkrFlexHoldingsSyncService, IbkrFlexHoldingsSyncService>();
        services.AddScoped<IIbkrFlexTradeSyncService, IbkrFlexTradeSyncService>();
        services.AddSingleton<BrokerageCostBasisReconciler>();
        services.AddScoped<IBrokerageHoldingsReader, BrokerageHoldingsReader>();
        // #673: published read port - the Integration adapter reaches tax lots only through it.
        services.AddScoped<ITaxLotReader, TaxLotReader>();
        // Shared with the Hangfire consecutive-failure-alert filter (Program.cs) — same
        // durable, storage-backed streak state, keyed separately per job/credential.
        services.TryAddSingleton<IJobFailureStreakStore, HangfireJobFailureStreakStore>();
        // Inzhur cabinet (design report: data/fs-inzhur-sync-design): the owner signs in from the connect modal
        // through a headless Chromium sidecar (reCAPTCHA v3 scores the real sign-in page); the daily job only
        // refreshes that session and reads the portfolio. Read-only: no trade or other write call to Inzhur exists.
        services.Configure<InzhurOptions>(config.GetSection(InzhurOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IInzhurCredentialRepository, InzhurCredentialRepository>();
        services.AddScoped<ICredentialRotationTarget, InzhurCredentialRotationTarget>();
        services.AddHttpClient<IInzhurApiClient, InzhurApiClient>((sp, client) =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(sp.GetRequiredService<IOptions<InzhurOptions>>().Value.UserAgent);
                client.Timeout = InzhurApiClient.RequestTimeout;
            })
            // The session's cookies are kept in the encrypted jar, not a handler-wide container shared across users.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
            .RedactLoggedHeaders(_ => true);
        services.AddSingleton<IInzhurBrowserLogin, PuppeteerInzhurBrowserLogin>();
        services.AddScoped<IInzhurSyncService, InzhurSyncService>();
        services.AddScoped<IInzhurConnector, InzhurConnector>();
        services.AddScoped<InzhurSyncJob>();
        services.AddScoped<IBrokerageConnectionStatusReader, BrokerageConnectionStatusReader>();

        services.AddScoped<IBKRSyncJob>();
        services.AddScoped<IbkrFlexIncrementalSyncJob>();
        services.AddScoped<IbkrFlexBackfillJob>();

        services.AddSingleton<IJobRegistrar, JobRegistrar>();

        return services;
    }
}
