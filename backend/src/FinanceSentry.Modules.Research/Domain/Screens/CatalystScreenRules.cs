namespace FinanceSentry.Modules.Research.Domain.Screens;

using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;

/// <summary>A reference-universe name that reports earnings inside the lookahead window.</summary>
public sealed record CatalystCandidate(string Ticker, DateOnly EarningsDate);

/// <summary>
/// The dated-catalyst screen (discovery funnel stage 5): names of the reference universe whose earnings date falls in
/// the next <c>LookaheadDays</c> days. Pure; the calendar is read elsewhere. Soonest first, then by ticker, so the
/// per-run cap keeps the nearest dates and a repeat run picks the same names.
/// </summary>
public static class CatalystScreenRules
{
    public static IReadOnlyList<CatalystCandidate> Select(
        IEnumerable<EarningsCalendarItem> items, IReadOnlySet<string> universe, DateOnly today, int lookaheadDays)
    {
        var last = today.AddDays(lookaheadDays);
        return [.. items
            .Where(i => string.Equals(i.EventType, EarningsEventType.Earnings, StringComparison.OrdinalIgnoreCase)
                && i.EventDate >= today && i.EventDate <= last)
            .Select(i => (Ticker: ReferenceListKeys.NormalizeTicker(i.Ticker), i.EventDate))
            .Where(i => i.Ticker is not null && universe.Contains(i.Ticker))
            .GroupBy(i => i.Ticker!, StringComparer.Ordinal)
            .Select(g => new CatalystCandidate(g.Key, g.Min(i => i.EventDate)))
            .OrderBy(c => c.EarningsDate)
            .ThenBy(c => c.Ticker, StringComparer.Ordinal)];
    }
}
