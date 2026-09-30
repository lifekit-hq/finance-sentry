namespace FinanceSentry.Modules.Research.Application.Services;

/// <summary>When a recurring job was first scheduled — how long a detector has had the chance to fire.</summary>
public interface IRecurringJobAgeReader
{
    /// <summary>The job's creation instant, or null when no recurring job with that id exists.</summary>
    DateTimeOffset? GetCreatedAt(string recurringJobId);
}
