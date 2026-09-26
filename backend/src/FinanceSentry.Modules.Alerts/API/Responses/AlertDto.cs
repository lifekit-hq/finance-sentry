namespace FinanceSentry.Modules.Alerts.API.Responses;

public record AlertDto(
    Guid Id,
    string Type,
    string Severity,
    string Title,
    string Message,
    Guid? ReferenceId,
    string? ReferenceLabel,
    bool IsRead,
    bool IsResolved,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    int OccurrenceCount,
    DateTimeOffset LastOccurredAt);
