namespace FinanceSentry.Modules.Research.Domain.Catalyst;

using System.Globalization;

/// <summary>
/// The pure rules of the catalyst detection (discovery funnel stage 7): which 8-K items count, how a nomination is tagged, in
/// what order nominations compete for the per-run cap, and when a filing is newer evidence than a removal. No read, no model
/// call, so the same filings always give the same decisions.
/// </summary>
public static class CatalystRules
{
    /// <summary>The form the scan reads. An amendment (<c>8-K/A</c>) restates a filing already seen and is not a new catalyst.</summary>
    public const string Form8K = "8-K";

    /// <summary>The prefix every catalyst nomination tag starts with; the first nomination reason is the scoreboard's screen tag.</summary>
    public const string TagPrefix = "catalyst: ";

    /// <summary>Which feed found a filing.</summary>
    public const string SourceFullText = "fulltext";

    public const string SourceSubmissions = "submissions";

    /// <summary>Splits an EDGAR items value (<c>"2.02,9.01"</c>, or an array already split) into trimmed, de-duplicated item codes.</summary>
    public static IReadOnlyList<string> ParseItems(IEnumerable<string?>? raw)
        => raw is null
            ? []
            : [.. raw
                .SelectMany(r => (r ?? string.Empty).Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(i => i.Trim())
                .Where(i => i.Length > 0)
                .Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The watched items a filing carries, in the order the scan ranks them (<paramref name="watched"/> order, first is the
    /// most wanted). Empty when none of its items is watched, so the filing is not a catalyst.
    /// </summary>
    public static IReadOnlyList<string> WatchedItems(IReadOnlyList<string> filingItems, IReadOnlyList<string> watched)
        => [.. watched.Where(w => filingItems.Contains(w, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The nomination tag for a filing's most wanted watched item: stable per item (not per filing), so re-nominations dedupe
    /// on the candidate and the scoreboard can group catalyst nominations by the item that fired.
    /// </summary>
    public static string Tag(string item) => TagPrefix + "8-K item " + item + ItemMeaning(item);

    /// <summary>True when a nomination reason is a catalyst nomination tag.</summary>
    public static bool IsCatalystTag(string? reason)
        => reason is not null && reason.StartsWith(TagPrefix, StringComparison.Ordinal);

    /// <summary>The filing in words, for the event row and the candidate-review summary.</summary>
    public static string Summary(IReadOnlyList<string> items, DateOnly filingDate)
        => string.Create(CultureInfo.InvariantCulture, $"{Form8K} item {string.Join(", ", items)} filed {filingDate:yyyy-MM-dd}");

    /// <summary>
    /// The order nominations compete for the cap in: the most wanted item first (its position in the watched list), then the
    /// newest filing, then the ticker, so a run that must drop a nomination always drops the same one.
    /// </summary>
    public static IEnumerable<T> Rank<T>(
        IEnumerable<T> filings, IReadOnlyList<string> watched, Func<T, IReadOnlyList<string>> items,
        Func<T, DateOnly> filingDate, Func<T, string> ticker)
        => filings
            .OrderBy(f => items(f).Select(i => IndexOf(watched, i)).DefaultIfEmpty(int.MaxValue).Min())
            .ThenByDescending(filingDate)
            .ThenBy(ticker, StringComparer.Ordinal);

    /// <summary>
    /// True when a filing is evidence the name's removal has not already overruled: the name was never removed, or the filing
    /// is dated after the day of the last removal. Day granularity, like the verdict readings of the coverage add rule
    /// (<c>CoverageRules.BuyDaysInWindow</c>): a filing dated the removal day may predate it, so it never brings the name back.
    /// </summary>
    public static bool IsNewerThanRemoval(DateOnly filingDate, DateTimeOffset? removedAt)
        => removedAt is not { } removed || filingDate > DateOnly.FromDateTime(removed.UtcDateTime);

    private static int IndexOf(IReadOnlyList<string> watched, string item)
    {
        for (var i = 0; i < watched.Count; i++)
        {
            if (string.Equals(watched[i], item, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static string ItemMeaning(string item) => item switch
    {
        "1.01" => " (material definitive agreement)",
        "2.02" => " (results of operations)",
        "5.02" => " (officer or director change)",
        "8.01" => " (other events)",
        _ => string.Empty,
    };
}
