namespace FinanceSentry.Modules.BrokerageSync;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Connections;
using FinanceSentry.Infrastructure.Http;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.BrokerageSync.Application.Connect;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Connections;
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
            // The session refresh, apart from the once-a-day read: sized from the session's lifetime (hourly).
            mgr.AddOrUpdate<InzhurKeepAliveJob>("inzhur-keepalive", job => job.ExecuteAsync(), inzhur.KeepAliveCron);
        }
    }

    public static IServiceCollection AddBrokerageSyncModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddConnectionHealthShadow(config);
        // Flex generates a statement on request, so its answers can be "not ready" for a while: not counted for 48 h
        // after the last success, and asked again every 2 h meanwhile. Configuration (ConnectionHealth:Sources:ibkr-flex)
        // overrides these. It runs once a day, so a counted failure is looked at again after 1 h and 4 h later
        // (EarlyRechecks) rather than reaching Failing on its third day.
        services.Configure<ConnectionHealthSourceOptions>(IbkrFlexFailureClassifier.ProviderName, source =>
        {
            source.EarlyRechecks = [TimeSpan.FromHours(1), TimeSpan.FromHours(4)];
            source.NotReadyDeadline = TimeSpan.FromHours(48);
            source.NotReadyRetryAfter = TimeSpan.FromHours(2);
        });
        services.AddUpdatedAtStamping();
        services.AddDbContext<BrokerageSyncDbContext>(
            (sp, o) => o.UseNpgsql(config.GetConnectionString("Default")!, b => b.MigrationsHistoryTable("__EFMigrationsHistory", "public")).UseUpdatedAtStamping(sp));
        services.AddOwnedRowEraser<BrokerageSyncDbContext>("BrokerageSync");
        services.AddScoped<IUserCredentialRevoker, BrokerageSyncCredentialRevoker>();

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
        services.AddHttpClient<IbkrOAuthClient>()
            .AddProviderResilience(ProviderHttpResilience.DefaultTimeout, maxRetries: 0);
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
        services.AddHttpClient<IIbkrFlexClient, IbkrFlexClient>()
            .AddProviderResilience(ProviderHttpResilience.DefaultTimeout, maxRetries: 0);
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
        services.AddScoped<IPositionLedgerReader, PositionLedgerReader>();
        // Shared with the Hangfire ConsecutiveFailureGaugeFilter (Program.cs) — same
        // durable, storage-backed streak state, keyed separately per job/credential.
        services.TryAddSingleton<IJobFailureStreakStore, HangfireJobFailureStreakStore>();
        // Inzhur cabinet (design report: data/fs-inzhur-sync-design): the owner signs in on inzhur.reit in his own
        // browser and pastes the session's refresh cookie (the sign-in's reCAPTCHA rejects this server's IP,
        // data/fs-inzhur-recaptcha); the keep-alive job only refreshes that session and the daily job refreshes it and
        // reads the portfolio. Read-only: no trade or other write call to Inzhur exists.
        services.Configure<InzhurOptions>(config.GetSection(InzhurOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IInzhurCredentialRepository, InzhurCredentialRepository>();
        services.AddScoped<ICredentialRotationTarget, InzhurCredentialRotationTarget>();
        services.AddHttpClient<IInzhurApiClient, InzhurApiClient>((sp, client) =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(sp.GetRequiredService<IOptions<InzhurOptions>>().Value.UserAgent);
                client.Timeout = InzhurApiClient.RequestTimeout;
            })
            .AddProviderResilience(InzhurApiClient.RequestTimeout)
            // The session's cookies are kept in the encrypted jar, not a handler-wide container shared across users.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
            .RedactLoggedHeaders(_ => true);
        services.AddScoped<IInzhurSyncService, InzhurSyncService>();
        services.AddScoped<IInzhurKeepAliveService, InzhurKeepAliveService>();
        services.AddSingleton<IInzhurSessionGate>(_ => new HangfireInzhurSessionGate(() => JobStorage.Current));
        services.AddScoped<IInzhurConnector, InzhurConnector>();
        services.AddScoped<InzhurSyncJob>();
        services.AddScoped<InzhurKeepAliveJob>();
        services.AddScoped<IBrokerageConnectionStatusReader, BrokerageConnectionStatusReader>();
        services.AddScoped<IAccountScopeChangeReader, BrokerageScopeChangeReader>();
        services.AddScoped<IBrokerageFlowReader, BrokerageFlowReader>();

        services.AddSingleton<IbkrFailureClassifier>();
        services.AddSingleton<IbkrFlexFailureClassifier>();
        services.AddScoped<IBKRSyncJob>();
        services.AddConnectionHealthProbe<IbkrConnectionHealthProbe>();
        services.AddScoped<IConnectionHealthReader, BrokerageConnectionHealthReader>();
        services.AddScoped<IbkrFlexIncrementalSyncJob>();
        services.AddScoped<IbkrFlexBackfillJob>();

        services.AddSingleton<IJobRegistrar, JobRegistrar>();

        return services;
    }
}
