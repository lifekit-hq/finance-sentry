namespace FinanceSentry.Infrastructure.Observability.Hangfire;

using global::Hangfire;

/// <summary>Per-job consecutive-failure streak state.</summary>
public readonly record struct JobFailureStreak(int Count, bool Alerted)
{
    public static readonly JobFailureStreak Empty = new(0, false);
}

/// <summary>
/// Durable store for consecutive-failure streaks so a restart doesn't reset a live streak. Keyed by job
/// name for <c>ConsecutiveFailureGaugeFilter</c>, and by credential for the crypto and IBKR sync jobs
/// (which also use <see cref="JobFailureStreak.Alerted"/>).
/// </summary>
public interface IJobFailureStreakStore
{
    JobFailureStreak Get(string jobName);

    void Set(string jobName, JobFailureStreak streak);
}

/// <summary>
/// Backs the streak state with Hangfire's own storage (a hash per job), so it lives in the same durable
/// PostgreSQL store as the jobs themselves — no extra app table/migration.
/// </summary>
public sealed class HangfireJobFailureStreakStore : IJobFailureStreakStore
{
    private const string KeyPrefix = "finance:jobfailure:";
    private const string CountField = "count";
    private const string AlertedField = "alerted";

    public JobFailureStreak Get(string jobName)
    {
        using var connection = JobStorage.Current.GetConnection();
        var hash = connection.GetAllEntriesFromHash(KeyPrefix + jobName);
        if (hash is null || hash.Count == 0)
            return JobFailureStreak.Empty;

        var count = hash.TryGetValue(CountField, out var c) && int.TryParse(c, out var parsed) ? parsed : 0;
        var alerted = hash.TryGetValue(AlertedField, out var a) && bool.TryParse(a, out var flag) && flag;
        return new JobFailureStreak(count, alerted);
    }

    public void Set(string jobName, JobFailureStreak streak)
    {
        using var connection = JobStorage.Current.GetConnection();
        connection.SetRangeInHash(KeyPrefix + jobName, new Dictionary<string, string>
        {
            [CountField] = streak.Count.ToString(),
            [AlertedField] = streak.Alerted.ToString(),
        });
    }
}
