using System.Globalization;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;

/// <summary>
/// The Revolut X trade cursor: the last instant (Unix ms, inclusive) every pair of the asset has
/// been walked to — <c>v1:&lt;ms&gt;</c>. Every pair is walked to the same instant in one call, so one
/// watermark covers them all, and a pair listed later has no fills before it.
/// </summary>
public static class RevolutXTradeCursor
{
    private const string Prefix = "v1:";

    /// <summary>The walked-to instant, or null for a never-walked (or unreadable) cursor.</summary>
    public static long? Parse(string? cursor) =>
        cursor is not null
        && cursor.StartsWith(Prefix, StringComparison.Ordinal)
        && long.TryParse(cursor.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
            ? ms
            : null;

    public static string Format(long walkedToMs) =>
        Prefix + walkedToMs.ToString(CultureInfo.InvariantCulture);
}
