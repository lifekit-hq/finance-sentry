namespace FinanceSentry.Modules.Research.Domain;

using System.Globalization;
using System.Text;

/// <summary>
/// When a macro event happens, whether it already has, and the key that names it across date corrections (#1091).
/// A row's <see cref="MacroEvent.EventDate"/>/<see cref="MacroEvent.EventTime"/> are the publisher's own wall
/// clock (BLS and the Fed publish in US Eastern time, the ECB in Frankfurt time), so the instant is resolved in that
/// zone, never in the server's or the reader's. An untimed row counts from the start of its local day, so it is
/// never announced as still to come on the day itself.
/// </summary>
public static class MacroEventTiming
{
    private static readonly TimeZoneInfo UsEastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo EuroArea = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private const string RateDecisionSuffix = " rate decision";

    /// <summary>The zone a region's publishers date their releases in; UTC for a region this calendar does not know.</summary>
    public static TimeZoneInfo ZoneFor(string region) => region.Trim().ToUpperInvariant() switch
    {
        "US" => UsEastern,
        "EU" => EuroArea,
        _ => TimeZoneInfo.Utc,
    };

    /// <summary>The UTC instant the event is released.</summary>
    public static DateTimeOffset EventAt(DateOnly date, TimeOnly? time, string region)
    {
        var local = date.ToDateTime(time ?? TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = ZoneFor(region).GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public static DateTimeOffset EventAt(MacroEvent e) => EventAt(e.EventDate, e.EventTime, e.Region);

    /// <summary><see cref="MacroEventStatus.Released"/> once the release instant is reached, otherwise scheduled.</summary>
    public static string StatusAt(DateTimeOffset eventAt, DateTimeOffset now)
        => eventAt <= now ? MacroEventStatus.Released : MacroEventStatus.Scheduled;

    /// <summary>
    /// The stable key of one release, <c>macro:{FAMILY}:{yyyy-MM}</c> (e.g. <c>macro:CPI:2026-10</c> for the September
    /// CPI released in October 2026). It names the release, not the row: a date corrected within the month keeps the
    /// key, which is what lets the sync recognise a misdated row and lets an announcement fire once per release.
    /// </summary>
    public static string Key(string eventName, DateOnly date)
        => $"macro:{Family(eventName)}:{date.ToString("yyyy-MM", CultureInfo.InvariantCulture)}";

    public static string Key(MacroEvent e) => Key(e.Event, e.EventDate);

    /// <summary>
    /// The release family of an event name: <c>CPI (Sep)</c> → <c>CPI</c>, <c>Nonfarm payrolls (Sep)</c> → <c>NFP</c>,
    /// <c>FOMC rate decision + SEP</c> → <c>FOMC</c>. Anything else is the name without its period and suffix, upper-cased.
    /// </summary>
    public static string Family(string eventName)
    {
        var head = eventName;
        var cut = head.IndexOfAny(['(', '+']);
        if (cut >= 0)
        {
            head = head[..cut];
        }

        head = head.Trim();
        if (head.StartsWith("Nonfarm payrolls", StringComparison.OrdinalIgnoreCase))
        {
            return "NFP";
        }

        if (head.EndsWith(RateDecisionSuffix, StringComparison.OrdinalIgnoreCase))
        {
            head = head[..^RateDecisionSuffix.Length];
        }

        var slug = new StringBuilder(head.Length);
        foreach (var c in head.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        return slug.ToString().TrimEnd('-');
    }
}

/// <summary>Whether a macro event is still to come or already out (#1091).</summary>
public static class MacroEventStatus
{
    public const string Scheduled = "scheduled";
    public const string Released = "released";

    public static bool IsKnown(string? status)
        => status is Scheduled or Released;
}
