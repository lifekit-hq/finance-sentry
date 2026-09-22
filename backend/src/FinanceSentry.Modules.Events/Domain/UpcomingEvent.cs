namespace FinanceSentry.Modules.Events.Domain;

/// <summary>One computed calendar row. Never stored - the calendar is a read over existing sources.</summary>
public sealed record UpcomingEvent(
    string Kind,
    DateOnly Date,
    TimeOnly? Time,
    string Subject,
    string Title,
    string? Detail,
    bool IsEstimate,
    string Source,
    Guid? ReferenceId);
