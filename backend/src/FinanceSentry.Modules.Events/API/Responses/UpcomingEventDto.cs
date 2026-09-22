namespace FinanceSentry.Modules.Events.API.Responses;

public sealed record UpcomingEventDto(
    string Kind,
    DateOnly Date,
    TimeOnly? Time,
    string Subject,
    string Title,
    string? Detail,
    bool IsEstimate,
    string Source,
    Guid? ReferenceId);

public sealed record EventSourceStatusDto(string Source, string Status);

public sealed record UpcomingEventsResult(
    IReadOnlyList<UpcomingEventDto> Items,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<EventSourceStatusDto> Sources);
