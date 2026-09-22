namespace FinanceSentry.Modules.Events.API.Responses;

public sealed record EventDeliveryDto(
    Guid EventId,
    string Disposition,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeliveredAt);

public sealed record EventVerdictDto(string Text, bool Notified, DateTimeOffset RecordedAt);

public sealed record FiredEventDto(
    Guid AlertId,
    string Kind,
    string Severity,
    string Subject,
    string Title,
    string Message,
    DateTimeOffset OccurredAt,
    bool IsRead,
    EventDeliveryDto? Delivery,
    EventVerdictDto? Verdict,
    string Outcome);

public sealed record FiredEventsPageResponse(
    IReadOnlyList<FiredEventDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
