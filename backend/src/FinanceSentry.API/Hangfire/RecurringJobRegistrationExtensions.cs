namespace FinanceSentry.API.Hangfire;

using global::Hangfire.PostgreSql;

public static class RecurringJobRegistrationExtensions
{
    /// <summary>Margin added on top of the storage lock timeout so a lock row that has just expired is reliably reclaimed.</summary>
    private static readonly TimeSpan BudgetMargin = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Adds background recurring-job registration. The retry budget is the registered
    /// <see cref="PostgreSqlStorageOptions.DistributedLockTimeout"/> plus a margin — a stale lock row
    /// cannot outlive it. Without PostgreSQL storage (in-memory test host) no such lock exists and the
    /// budget is just the margin.
    /// </summary>
    public static IServiceCollection AddRecurringJobRegistration(this IServiceCollection services)
    {
        services.AddSingleton<RecurringJobRegistrationStatus>();
        services.AddSingleton(sp => new RecurringJobRegistrationOptions(
            (sp.GetService<PostgreSqlStorageOptions>()?.DistributedLockTimeout ?? TimeSpan.Zero) + BudgetMargin,
            InitialDelay,
            MaxDelay));
        services.AddHostedService<RecurringJobRegistrationService>();

        return services;
    }
}
