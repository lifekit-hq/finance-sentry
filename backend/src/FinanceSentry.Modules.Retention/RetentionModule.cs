namespace FinanceSentry.Modules.Retention;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Retention.Application;
using FinanceSentry.Modules.Retention.Application.Services;
using FinanceSentry.Modules.Retention.Domain;
using FinanceSentry.Modules.Retention.Infrastructure;
using FinanceSentry.Modules.Retention.Infrastructure.Backup;
using FinanceSentry.Modules.Retention.Infrastructure.Jobs;
using FinanceSentry.Modules.Retention.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FinanceSentry.Infrastructure.Persistence;

/// <summary>
/// Cross-cutting retention &amp; backup module (feature 024). Owns the <c>retention</c> schema and the
/// scheduled purge/backup/restore-verify jobs. Enforces policy for tables that are not already
/// governed by a bespoke module job; those remain untouched and are documented in the registry.
/// </summary>
public static class RetentionModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddRetentionModule(config);
    }

    private sealed class JobRegistrar : IJobRegistrar
    {
        public void RegisterJobs(IServiceProvider sp)
        {
            var mgr = sp.GetRequiredService<IRecurringJobManager>();
            var retention = sp.GetRequiredService<IOptions<RetentionOptions>>().Value;

            // Nightly generic purge of out-of-policy rows (US1).
            mgr.AddOrUpdate<RetentionPurgeJob>(
                "retention-purge", job => job.RunAsync(false, CancellationToken.None), Cron.Daily(retention.PurgeHourUtc));

            // Nightly off-host backup + weekly restore drill (US2).
            var backup = sp.GetRequiredService<IOptions<BackupOptions>>().Value;
            mgr.AddOrUpdate<BackupJob>(
                "db-backup", job => job.RunAsync(CancellationToken.None), Cron.Daily(backup.BackupHourUtc));
            mgr.AddOrUpdate<RestoreVerifyJob>(
                "db-restore-verify", job => job.RunAsync(CancellationToken.None), Cron.Weekly());

            // Downsampling (US3, P2) — scheduled but inert until Retention:Downsample:Enabled.
            mgr.AddOrUpdate<DownsampleJob>(
                "retention-downsample", job => job.RunAsync(CancellationToken.None), Cron.Daily(retention.DownsampleHourUtc));
        }
    }

    public static IServiceCollection AddRetentionModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddUpdatedAtStamping();
        services.AddDbContext<RetentionDbContext>(
            (sp, o) => o.UseNpgsql(
                config.GetConnectionString("Default")!,
                b => b.MigrationsHistoryTable("__ef_migrations_history_retention", "public")).UseUpdatedAtStamping(sp));

        services.Configure<RetentionOptions>(config.GetSection(RetentionOptions.SectionName));
        services.Configure<BackupOptions>(config.GetSection(BackupOptions.SectionName));

        services.AddScoped<RetentionPurgeService>();
        services.AddScoped<RetentionPurgeJob>();
        services.AddScoped<DownsampleService>();
        services.AddScoped<DownsampleJob>();

        // US2 backups. The R2 store is a singleton (holds one S3 client); dump/restore are scoped.
        services.AddSingleton<IBackupStore, S3BackupStore>();
        services.AddScoped<PgDumpRunner>();
        services.AddScoped<RestoreVerifier>();
        services.AddScoped<BackupJob>();
        services.AddScoped<RestoreVerifyJob>();

        services.AddHostedService<RetentionMetricsPrimer>();
        services.AddSingleton<IJobRegistrar, JobRegistrar>();

        return services;
    }
}
