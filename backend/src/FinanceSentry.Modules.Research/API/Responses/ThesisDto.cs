namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Modules.Research.Domain;

public record ThesisDto(
    Guid Id,
    string Ticker,
    string ThesisText,
    IReadOnlyList<ThesisDataPoint> KeyDataPoints,
    IReadOnlyList<ThesisCatalyst> Catalysts,
    IReadOnlyList<ThesisInvalidationTrigger> InvalidationTriggers,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? BrokenAt,
    string? BrokenReason,
    decimal? EntryPrice = null,
    // Who made the last write: "User" (the owner's session) or "Ledger" (its service token).
    string Origin = "User",
    // Free-text kill-switches outside the metric vocabulary; model-checked on each review, never monitored.
    IReadOnlyList<string>? NarrativeFalsifiers = null);
