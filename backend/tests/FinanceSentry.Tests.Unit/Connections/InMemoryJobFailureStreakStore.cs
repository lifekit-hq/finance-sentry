namespace FinanceSentry.Tests.Unit.Connections;

using FinanceSentry.Infrastructure.Observability.Hangfire;

/// <summary>The legacy per-credential streak a shadow-mode sync job keeps, in memory.</summary>
internal sealed class InMemoryJobFailureStreakStore : IJobFailureStreakStore
{
    private readonly Dictionary<string, JobFailureStreak> _streaks = [];

    public JobFailureStreak Get(string jobName) =>
        _streaks.TryGetValue(jobName, out var streak) ? streak : JobFailureStreak.Empty;

    public void Set(string jobName, JobFailureStreak streak) => _streaks[jobName] = streak;
}
