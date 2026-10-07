namespace FinanceSentry.Modules.Companion.API.Responses;

using System.Text.Json.Serialization;

/// <param name="AppUrl">Absolute link to the app page the event is about; absent when the event has no target or no
/// public base URL is configured.</param>
public record CompanionEventDto(
    Guid Id,
    string Kind,
    string Subject,
    string Severity,
    string Summary,
    Guid? ReferenceId,
    string Disposition,
    DateTimeOffset OccurredAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AppUrl = null);

/// <summary>Envelope for the agent's pull of undelivered companion events (feature 031).</summary>
public record CompanionEventsResult(
    IReadOnlyList<CompanionEventDto> Events,
    string Mode,
    DateTimeOffset RetrievedAt,
    string? Note = null);
