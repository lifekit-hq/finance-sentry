namespace FinanceSentry.Modules.Companion;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain.Ports;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FinanceSentry.Modules.Companion.Infrastructure.Jobs;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Companion.Infrastructure.Services;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FinanceSentry.Infrastructure.Persistence;

public static class CompanionModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddCompanionModule(config);
    }

    private sealed class JobRegistrar : IJobRegistrar
    {
        public void RegisterJobs(IServiceProvider sp)
        {
            var mgr = sp.GetRequiredService<IRecurringJobManager>();

            // Capture then dispatch, both every minute (feature 031, R8) — meets the 60s realtime bar.
            mgr.AddOrUpdate<CompanionCaptureJob>(
                "companion-capture", job => job.ExecuteAsync(CancellationToken.None), "* * * * *");

            mgr.AddOrUpdate<CompanionDispatchJob>(
                "companion-dispatch", job => job.ExecuteAsync(CancellationToken.None), "* * * * *");

            // Digest trigger runs hourly; fires per user only at their local digest hour (feature 031, US3).
            mgr.AddOrUpdate<CompanionDigestJob>(
                "companion-digest", job => job.ExecuteAsync(CancellationToken.None), "0 * * * *");

            // Web Push sender (spec 859): independent of the dispatch job and of the notification mode.
            mgr.AddOrUpdate<CompanionPushJob>(
                "companion-push", job => job.ExecuteAsync(CancellationToken.None), "* * * * *");

            // Problem reports to the fleet inbox: a no-op until the relay key is set up, so reports wait pending.
            mgr.AddOrUpdate<ProblemReportForwardJob>(
                "problem-reports-forward", job => job.ExecuteAsync(CancellationToken.None), "* * * * *");
        }
    }

    public static IServiceCollection AddCompanionModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddUpdatedAtStamping();
        services.AddDbContext<CompanionDbContext>(
            (sp, o) => o.UseNpgsql(
                config.GetConnectionString("Default")!,
                b => b.MigrationsHistoryTable("__ef_migrations_history_companion", "public")).UseUpdatedAtStamping(sp));

        services.Configure<CompanionOptions>(config.GetSection(CompanionOptions.SectionName));

        services.Configure<WebPushOptions>(config.GetSection(WebPushOptions.SectionName));

        services.AddScoped<IPushSubscriptionRepository, PushSubscriptionRepository>();
        services.AddScoped<IPushDeliveryRepository, PushDeliveryRepository>();
        services.AddScoped<INotificationSettingRepository, NotificationSettingRepository>();
        services.AddScoped<ICompanionEventRepository, CompanionEventRepository>();
        services.AddScoped<ICompanionCaptureStateRepository, CompanionCaptureStateRepository>();

        services.AddSingleton<IMaterialityPolicy, MaterialityPolicy>();
        services.AddScoped<ICompanionEventCapture, CompanionEventCapture>();
        services.AddScoped<ISyncFailureReconciler, SyncFailureReconciler>();
        // #673: published read port - the Integration adapter reaches the outbox only through it.
        services.AddScoped<IOutboxDeliveryReader, OutboxDeliveryReader>();
        services.AddScoped<IAgentWakeDispatcher, WebhookAgentWakeDispatcher>();

        services.AddHttpClient(WebhookAgentWakeDispatcher.HttpClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(10));

        services.AddScoped<IPushSender, WebPushSender>();
        // No redirects: an allow-listed push service must not be able to bounce the request to another host.
        services.AddHttpClient(WebPushSender.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        services.Configure<ProblemReportOptions>(config.GetSection(ProblemReportOptions.SectionName));
        services.AddScoped<IProblemReportRepository, ProblemReportRepository>();
        services.AddSingleton<IProblemReportRelay, SshProblemReportRelay>();
        services.AddScoped<ProblemReportForwardJob>();

        services.AddScoped<CompanionPushJob>();
        services.AddScoped<CompanionCaptureJob>();
        services.AddScoped<CompanionDispatchJob>();
        services.AddScoped<CompanionDigestJob>();

        services.AddSingleton<IJobRegistrar, JobRegistrar>();

        return services;
    }
}
