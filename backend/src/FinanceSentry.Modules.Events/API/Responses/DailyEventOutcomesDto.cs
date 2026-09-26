namespace FinanceSentry.Modules.Events.API.Responses;

public sealed record DailyEventOutcomeDto(
    Guid EventId,
    Guid? AlertId,
    string Kind,
    string Subject,
    DateTimeOffset OccurredAt,
    string Disposition,
    EventVerdictDto? Verdict,
    string Outcome);

/// <summary>
/// Every companion event that fired for a user on one UTC day, across every kind - not the narrow
/// alert-backed subset the fired-events feed covers - with the recorded outcome per event (feature 687).
/// <see cref="Judged"/> is <see cref="Sent"/> plus <see cref="Withheld"/>: every event with a recorded
/// verdict, whether or not it was told to the user.
/// </summary>
public sealed record DailyEventOutcomesResponse(
    DateOnly Date,
    IReadOnlyList<DailyEventOutcomeDto> Items,
    int Fired,
    int Judged,
    int Sent,
    int Withheld,
    DateTimeOffset RetrievedAt);
