namespace FinanceSentry.Modules.Companion.API.Responses;

public record CompanionEventDto(
    Guid Id,
    string Kind,
    string Subject,
    string Severity,
    string Summary,
    Guid? ReferenceId,
    string Disposition,
    DateTimeOffset OccurredAt);

/// <summary>Envelope for the agent's pull of undelivered companion events (feature 031).</summary>
public record CompanionEventsResult(
    IReadOnlyList<CompanionEventDto> Events,
    string Mode,
    DateTimeOffset RetrievedAt,
    string? Note = null);
