namespace FinanceSentry.Modules.BankSync.Infrastructure.Categorization;

using FinanceSentry.Core.Domain;

/// <summary>
/// Pure matching logic shared by <see cref="CategoryResolver"/> and its tests: matches a
/// free-text description (case-insensitively) against a keyword list, longest keyword first
/// so a more specific match ("uber eats") wins over a shorter substring ("uber").
/// </summary>
public static class MerchantKeywordMatcher
{
    /// <summary>
    /// Orders keywords longest-first and lowercases them for matching. Call once at load time.
    /// </summary>
    public static IReadOnlyList<(string Keyword, string CategoryKey)> Prepare(
        IEnumerable<(string Keyword, string CategoryKey)> keywords)
        => keywords
            .Select(k => (Keyword: k.Keyword.ToLowerInvariant(), k.CategoryKey))
            .OrderByDescending(k => k.Keyword.Length)
            .ToList();

    /// <summary>
    /// Returns the category of the first (longest) keyword contained in the description,
    /// or UNCATEGORIZED when none match. <paramref name="prepared"/> must come from
    /// <see cref="Prepare"/> (lowercased, longest-first).
    /// </summary>
    public static string Resolve(
        string? description, IReadOnlyList<(string Keyword, string CategoryKey)> prepared)
    {
        if (string.IsNullOrWhiteSpace(description))
            return CategoryKeys.Uncategorized;

        var haystack = description.ToLowerInvariant();
        foreach (var (keyword, categoryKey) in prepared)
        {
            if (ContainsKeyword(haystack, keyword))
                return categoryKey;
        }
        return CategoryKeys.Uncategorized;
    }

    // A keyword with a trailing space ("sky ", "eir ") names a whole word, so it must not start
    // mid-word: "esky eskyie" (a flights agency) is not "sky ", nor "their " "eir ".
    private static bool ContainsKeyword(string haystack, string keyword)
    {
        if (keyword[^1] != ' ' || !char.IsLetterOrDigit(keyword[0]))
            return haystack.Contains(keyword, StringComparison.Ordinal);

        var from = 0;
        while (from < haystack.Length)
        {
            var at = haystack.IndexOf(keyword, from, StringComparison.Ordinal);
            if (at < 0)
                return false;
            if (at == 0 || !char.IsLetterOrDigit(haystack[at - 1]))
                return true;
            from = at + 1;
        }
        return false;
    }
}
