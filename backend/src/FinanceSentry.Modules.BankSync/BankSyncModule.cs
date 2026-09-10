namespace FinanceSentry.Modules.BankSync;

using FinanceSentry.Infrastructure.Logging;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.AuditLog;
using FinanceSentry.Modules.BankSync.Infrastructure.Categorization;
using FinanceSentry.Modules.BankSync.Infrastructure.FeatureFlags;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FinanceSentry.Modules.BankSync.Infrastructure.Performance;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FinanceSentry.Modules.BankSync.Infrastructure.Services;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Infrastructure.Encryption;
using Microsoft.Extensions.Options;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class BankSyncModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddBankSyncModule(config);
    }

    private sealed class JobRegistrar : IJobRegistrar
    {
        public void RegisterJobs(IServiceProvider sp)
        {
            var mgr = sp.GetRequiredService<IRecurringJobManager>();
            var jobs = sp.GetRequiredService<IBackgroundJobClient>();

            mgr.AddOrUpdate<SyncScheduler>(
                "bank-account-sync-scheduler",
                s => s.ScheduleAllActiveAccounts(CancellationToken.None),
                "*/10 * * * *");

            mgr.AddOrUpdate<DataRetentionJob>(
                "data-retention",
                job => job.RunAsync(false, CancellationToken.None),
                Cron.Monthly());

            mgr.AddOrUpdate<CredentialBackupJob>(
                "credential-backup",
                job => job.RunAsync(CancellationToken.None),
                Cron.Weekly());

            // Retired by 044: CategorySpikeDetectionJob supersedes it. Hangfire keeps recurring
            // definitions in storage, so an already-deployed schedule has to be withdrawn by name.
            mgr.RemoveIfExists("unusual-spend-detection");

            mgr.AddOrUpdate<SubscriptionDetectionJob>(
                "subscription-detection",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily());

            mgr.AddOrUpdate<PriceHikeDetectionJob>(
                "price-hike-detection",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily());
            mgr.AddOrUpdate<DuplicateChargeDetectionJob>(
                "duplicate-charge-detection",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily());
            mgr.AddOrUpdate<CategorySpikeDetectionJob>(
                "category-spike-detection",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily());
            mgr.AddOrUpdate<FxSpreadDetectionJob>(
                "fx-spread-detection",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily());

            mgr.AddOrUpdate<StaleSyncReaperJob>(
                "stale-sync-reaper",
                job => job.ReapAsync(),
                "*/15 * * * *");

            mgr.AddOrUpdate<ConsentExpiryReminderJob>(
                "consent-expiry-reminder",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily());

            // Startup sweep: any job still "running"/account still "syncing" after a restart was
            // orphaned mid-sync and would otherwise deadlock the scheduler — reap them all first,
            // then (re)schedule the active accounts.
            jobs.Enqueue<StaleSyncReaperJob>(
                job => job.ExecuteAsync(true, CancellationToken.None));

            jobs.Enqueue<SyncScheduler>(
                s => s.ScheduleAllActiveAccounts(CancellationToken.None));
        }
    }


    public static IServiceCollection AddBankSyncModule(
        this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")!;

        services.AddDbContext<BankSyncDbContext>(o => o.UseNpgsql(connectionString, b => b.MigrationsHistoryTable("__EFMigrationsHistory", "public")));

        // #493: validated AT STARTUP, not at first credential read. Outside Development an
        // unconfigured or disclosed key stops the process coming up; the service itself no longer
        // falls back to the key committed in this repository.
        services.AddOptions<EncryptionOptions>()
            .Bind(config.GetSection(EncryptionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EncryptionOptions>, EncryptionOptionsValidator>();
        services.Configure<HygieneSentinelsOptions>(config.GetSection(HygieneSentinelsOptions.SectionName));
        services.AddSingleton<ICredentialEncryptionService, CredentialEncryptionService>();

        // #493: rows written under the old key are re-encrypted on startup. Registered here
        // because this module owns the encryption registration; every module contributes its own
        // stores as ICredentialRotationTarget and they are discovered by that interface.
        services.AddScoped<CredentialKeyRotationService>();
        services.AddHostedService<CredentialKeyRotationHostedService>();
        services.AddScoped<ICredentialRotationTarget, MonobankCredentialRotationTarget>();
        services.AddScoped<ICredentialRotationTarget, TrueLayerConnectionRotationTarget>();

        services.AddScoped<IBankAccountRepository, BankAccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ISyncJobRepository, SyncJobRepository>();
        services.AddScoped<IMonobankCredentialRepository, MonobankCredentialRepository>();
        services.AddScoped<ITrueLayerConnectionRepository, TrueLayerConnectionRepository>();

        var deduplicationKey = config["Deduplication:MasterKeyBase64"]
            ?? throw new InvalidOperationException("Deduplication:MasterKeyBase64 is required.");
        services.AddSingleton<ITransactionDeduplicationService>(
            _ => new TransactionDeduplicationService(deduplicationKey));

        services.AddSingleton<ICategoryResolver, CategoryResolver>();
        // Stateless over the singleton resolver — the per-user installment plans are a call
        // argument, never cached here.
        services.AddSingleton<ITransactionCategorizer, TransactionCategorizer>();
        services.AddScoped<ICategoryReadService, CategoryReadService>();

        services.AddHttpClient<MonobankHttpClient>(client =>
            client.BaseAddress = new Uri(config["Monobank:BaseUrl"] ?? "https://api.monobank.ua"));
        services.AddSingleton<MonobankBalanceCache>();
        services.AddScoped<IMonobankAdapter, MonobankAdapter>();
        services.AddScoped<MonobankAdapter>();
        services.AddScoped<IBankProvider>(sp => sp.GetRequiredService<MonobankAdapter>());

        var trueLayerAuthBase = new Uri(config["TrueLayer:AuthBaseUrl"] ?? "https://auth.truelayer-sandbox.com");
        var trueLayerApiBase = new Uri(config["TrueLayer:ApiBaseUrl"] ?? "https://api.truelayer-sandbox.com");
        services.AddHttpClient(TrueLayerHttpClient.AuthClientName, c => c.BaseAddress = trueLayerAuthBase);
        services.AddHttpClient(TrueLayerHttpClient.ApiClientName, c => c.BaseAddress = trueLayerApiBase);
        services.AddSingleton<ITrueLayerClient, TrueLayerHttpClient>();
        services.AddSingleton<TrueLayerCategoryMapper>();
        services.AddScoped<TrueLayerAdapter>();
        services.AddScoped<IBankProvider>(sp => sp.GetRequiredService<TrueLayerAdapter>());

        services.AddScoped<IBankProviderFactory, BankProviderFactory>();

        services.AddSingleton<CorrelationIdAccessor>();
        services.AddScoped<ICorrelationIdAccessor>(sp => sp.GetRequiredService<CorrelationIdAccessor>());
        services.AddScoped<IBankSyncLogger, BankSyncLogger>();
        services.AddScoped<IScheduledSyncService, ScheduledSyncService>();
        services.AddScoped<ITransactionSyncCoordinator, TransactionSyncCoordinator>();

        services.AddScoped<IAggregationService, AggregationService>();
        services.AddScoped<ICommittedOutflowPolicy, CommittedOutflowPolicy>();
        services.AddScoped<IMoneyFlowStatisticsService, MoneyFlowStatisticsService>();
        services.AddScoped<IMerchantCategoryStatisticsService, MerchantCategoryStatisticsService>();
        services.AddScoped<IDashboardQueryService, DashboardQueryService>();
        services.AddScoped<IFlowBreakdownService, FlowBreakdownService>();
        services.AddScoped<ITransferDetectionService, TransferDetectionService>();

        services.AddScoped<ICounterpartyRepository, CounterpartyRepository>();
        services.AddScoped<ICounterpartyClassificationService, CounterpartyClassificationService>();
        services.AddScoped<ICommittedMerchantPinRepository, CommittedMerchantPinRepository>();

        services.AddScoped<IBankingAccountsReader, BankingAccountsReader>();
        services.AddScoped<IBankingTransactionReader, BankingTransactionReader>();
        services.AddScoped<IBankingTotalsReader, BankingTotalsReader>();
        services.AddScoped<IMerchantSpendingReader, MerchantSpendingReader>();

        services.AddScoped<ScheduledSyncJob>();
        services.AddScoped<SyncScheduler>();
        services.AddScoped<DataRetentionJob>();
        services.AddScoped<CredentialBackupJob>();
        services.AddScoped<SubscriptionDetectionJob>();
        services.AddScoped<StaleSyncReaperJob>();
        services.AddScoped<ConsentExpiryReminderJob>();
        services.AddScoped<PriceHikeDetectionJob>();
        services.AddScoped<DuplicateChargeDetectionJob>();
        services.AddScoped<CategorySpikeDetectionJob>();
        services.AddScoped<FxSpreadDetectionJob>();

        services.AddSingleton<IFeatureFlagService, FeatureFlagService>();
        services.AddSingleton<IAuditLogService, AuditLogService>();
        services.AddScoped<EFQueryLoggerInterceptor>();

        services.AddSingleton<IJobRegistrar, JobRegistrar>();

        return services;
    }
}
