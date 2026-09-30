namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Modules.Research.Application.Services;
using Hangfire;
using Hangfire.Storage;

/// <summary>
/// Reads a recurring job's creation time from Hangfire's own storage (the same durable PostgreSQL
/// store the jobs live in), so no extra table records when a detector went live.
/// </summary>
public sealed class HangfireRecurringJobAgeReader : IRecurringJobAgeReader
{
    public DateTimeOffset? GetCreatedAt(string recurringJobId)
    {
        using var connection = JobStorage.Current.GetConnection();
        var job = connection.GetRecurringJobs([recurringJobId]).FirstOrDefault();

        // Hangfire stores recurring-job timestamps as UTC without a kind.
        return job?.CreatedAt is { } created
            ? new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc))
            : null;
    }
}
