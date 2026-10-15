namespace FinanceSentry.Modules.Research.API.Responses;

/// <summary>
/// One macro release. <paramref name="EventDate"/>/<paramref name="EventTime"/> are the publisher's local wall clock
/// (US Eastern for BLS and the Fed, Frankfurt for the ECB); <paramref name="EventAt"/> is the same instant in UTC.
/// <paramref name="Status"/> is <c>scheduled</c> until <paramref name="EventAt"/> and <c>released</c> from then on
/// (#1091) — a lookahead must only announce a scheduled event. <paramref name="EventKey"/> names the release stably
/// (<c>macro:CPI:2026-10</c>), for deduplicating announcements.
/// </summary>
public record MacroEventDto(
    Guid Id,
    DateOnly EventDate,
    TimeOnly? EventTime,
    string Event,
    string Region,
    string Importance,
    string Source,
    DateTimeOffset EventAt,
    string Status,
    string EventKey);
