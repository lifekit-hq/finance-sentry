namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using Hangfire;
using Hangfire.InMemory;
using Hangfire.PostgreSql;
using Hangfire.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

public static class HangfireSetup
{
    // Dedicated schema keeps Hangfire's tables out of the app's EF migrations (FR-010).
    private const string HangfireSchema = "hangfire";

    public static IServiceCollection AddHangfireServices(
        this IServiceCollection services,
        IConfiguration config,
        IHostEnvironment environment)
    {
        // Registered so consumers (startup job registration) derive their lock-wait budget from the
        // effective DistributedLockTimeout instead of repeating its value.
        var storageOptions = new PostgreSqlStorageOptions
        {
            SchemaName = HangfireSchema,
            PrepareSchemaIfNecessary = true,
            QueuePollInterval = TimeSpan.FromSeconds(15),
        };
        if (!environment.IsEnvironment("Testing"))
            services.AddSingleton(storageOptions);

        services.AddHangfire(cfg =>
        {
            // FR-004 (feature 024): Hangfire auto-expires succeeded jobs after ~1 day by default, so the
            // `hangfire` schema stays bounded without extra configuration. Loki (30d) + Prometheus (30d)
            // retention and the capped Serilog file sink bound the remaining log/metric growth.

            // Tests run without a reachable database — keep them on in-memory storage so the host
            // still boots. Every other environment uses durable PostgreSQL storage.
            if (environment.IsEnvironment("Testing"))
            {
                cfg.UseInMemoryStorage(new InMemoryStorageOptions
                {
                    MaxExpirationTime = TimeSpan.FromHours(24),
                });
                return;
            }

            // Durable job storage on the existing PostgreSQL so job history + recurring-job state
            // survive restarts (FR-010) — the prerequisite for job-health trends (US3) and
            // consecutive-failure tracking (US4). Replaces the in-memory storage that lost everything.
            var connectionString = config.GetConnectionString("Default")
                ?? throw new InvalidOperationException("Connection string 'Default' is required for Hangfire storage.");

            cfg.UsePostgreSqlStorage(
                options => options.UseNpgsqlConnection(connectionString),
                storageOptions);
        });

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = 2;
            options.Queues = ["default"];
        });

        return services;
    }
}

/// <summary>The recurring per-account sync job's id: one place for the scheduler, reconciliation and disconnect paths.</summary>
public static class SyncAccountJob
{
    public const string IdPrefix = "sync-account-";

    public static string IdFor(Guid accountId) => $"{IdPrefix}{accountId}";
}

public class SyncScheduler(
    IBankAccountRepository accounts,
    IRecurringJobManager recurringJobs,
    JobStorage jobStorage,
    IAccountDiscoveryService accountDiscovery,
    ILogger<SyncScheduler> logger)
{
    private const string PerAccountCron = "*/30 * * * *";

    private readonly IBankAccountRepository _accounts = accounts;
    private readonly IRecurringJobManager _recurringJobs = recurringJobs;
    private readonly JobStorage _jobStorage = jobStorage;
    private readonly IAccountDiscoveryService _accountDiscovery = accountDiscovery;
    private readonly ILogger<SyncScheduler> _logger = logger;

    public async Task ScheduleAllActiveAccounts(CancellationToken ct = default)
    {
        // Re-list provider accounts per connection and create rows for anything not yet known
        // BEFORE reading active accounts below, so a newly discovered account is scheduled in
        // this same pass rather than waiting for the next one.
        try
        {
            await _accountDiscovery.DiscoverNewAccountsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Account discovery failed for this scheduled run; existing accounts will still be scheduled.");
        }

        var activeAccounts = await _accounts.GetAllActiveUnscopedAsync(ct);
        var activeIds = new HashSet<Guid>(activeAccounts.Select(a => a.Id));

        foreach (var account in activeAccounts)
        {
            _recurringJobs.AddOrUpdate<ScheduledSyncJob>(
                SyncAccountJob.IdFor(account.Id),
                job => job.ExecuteSyncAsync(account.Id),
                PerAccountCron);
        }

        RemoveJobsOfInactiveAccounts(activeIds);
    }

    // A deactivated or deleted account stops being listed above, but its recurring job would otherwise keep firing
    // (and failing) every 30 minutes for good.
    private void RemoveJobsOfInactiveAccounts(HashSet<Guid> activeIds)
    {
        List<string> scheduledIds;
        using (var connection = _jobStorage.GetConnection())
        {
            scheduledIds = [.. connection.GetRecurringJobs()
                .Select(job => job.Id)
                .Where(id => id.StartsWith(SyncAccountJob.IdPrefix, StringComparison.Ordinal))];
        }

        foreach (var jobId in scheduledIds)
        {
            if (Guid.TryParse(jobId.AsSpan(SyncAccountJob.IdPrefix.Length), out var accountId) && activeIds.Contains(accountId))
                continue;

            _recurringJobs.RemoveIfExists(jobId);
            _logger.LogInformation("Removed recurring sync job {JobId}: its account is gone or no longer active.", jobId);
        }
    }
}
