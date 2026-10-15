namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Published read port (#673): the macro calendar across every region and importance, for
/// cross-module readers such as the Events module. Implemented inside Research; the
/// FinanceSentry.Integration adapter reaches Research only through this interface.
/// </summary>
public interface IMacroCalendarReader
{
    /// <summary>Every macro event dated within [<paramref name="from"/>, <paramref name="to"/>].</summary>
    Task<IReadOnlyList<MacroCalendarItem>> QueryAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>
/// One macro calendar event as a cross-module reader sees it. <paramref name="EventAt"/> is the UTC release instant,
/// <paramref name="Status"/> is <c>scheduled</c> until then and <c>released</c> after (#1091), and
/// <paramref name="EventKey"/> names the release stably (<c>macro:CPI:2026-10</c>).
/// </summary>
public sealed record MacroCalendarItem(
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
