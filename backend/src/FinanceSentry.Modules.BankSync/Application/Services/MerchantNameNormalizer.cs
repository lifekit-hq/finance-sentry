namespace FinanceSentry.Modules.BankSync.Application.Services;

using System.Text.RegularExpressions;

public static class MerchantNameNormalizer
{
    /// <summary>
    /// The key every unnameable merchant collapses to. Public because callers that turn user
    /// input into a key have to reject it — a rule keyed on <c>unknown</c> would claim the whole
    /// unnamed tail of the book.
    /// </summary>
    public const string UnknownKey = "unknown";

    private static readonly string[] DomainSuffixes = [".com", ".net", ".io", ".co", ".org"];
    private static readonly Regex TrailingNumericPattern = new(@"[\s\-_*#]+\d[\d\s\-_]*$", RegexOptions.Compiled);
    private static readonly Regex CollapseSpacesPattern = new(@"\s+", RegexOptions.Compiled);

    // Bank statements spell the same recurring merchant differently every month
    // (e.g. "Anthropic* Claude Sub", "Claude.ai Subscription", "Anthropic Ireland"),
    // which fragments them below the recurrence threshold. Collapse known brands to
    // one canonical key so their charges group together. Keep this list narrow —
    // only brands whose descriptions actually vary — to avoid over-merging.
    private static readonly (string Keyword, string Canonical)[] BrandAliases =
    [
        ("anthropic", "claude"),
        ("claude", "claude"),
        ("openai", "openai"),
        ("chatgpt", "openai"),
    ];

    private static readonly Regex MobiTopUpPattern =
        new(@"^\*?\s*mobi\s+top-?up\s+(\d{4,})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The merchant key recurring-charge detection groups by, and the key stored on
    /// <c>DetectedSubscription.MerchantNameNormalized</c>. Anything that needs to ask "is this
    /// transaction one of the user's detected commitments?" must derive its key here — a plain
    /// <see cref="Normalize"/> of the merchant name would miss the description fallback and the
    /// mobile top-up collapsing, and so would systematically under-match.
    /// </summary>
    public static string NormalizeDetectionKey(string? merchantName, string? description)
    {
        var raw = merchantName ?? description;
        if (raw is not null)
        {
            // Mobile top-ups carry the phone number in the description
            // ("*MOBI TOP-UP 0857860057"), which both fragments the merchant key and trips the
            // generic top-up blocklist — collapse them to a stable per-number key instead so a
            // monthly top-up is tracked like any other recurring cost.
            var mobi = MobiTopUpPattern.Match(raw.Trim());
            if (mobi.Success)
            {
                var number = mobi.Groups[1].Value;
                return $"mobile top-up {number[^4..]}";
            }
        }

        return Normalize(raw);
    }

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return UnknownKey;

        var result = input.Trim().ToLowerInvariant();

        if (result.StartsWith("paypal*", StringComparison.Ordinal))
            result = result["paypal*".Length..];

        foreach (var suffix in DomainSuffixes)
        {
            if (result.EndsWith(suffix, StringComparison.Ordinal))
            {
                result = result[..^suffix.Length];
                break;
            }
        }

        result = result.TrimStart('*', '#', ' ');

        result = TrailingNumericPattern.Replace(result, string.Empty);

        result = CollapseSpacesPattern.Replace(result, " ").Trim();

        if (string.IsNullOrWhiteSpace(result))
            return UnknownKey;

        foreach (var (keyword, canonical) in BrandAliases)
        {
            if (result.Contains(keyword, StringComparison.Ordinal))
                return canonical;
        }

        return result;
    }

    public static string GetDisplayName(IEnumerable<string?> rawNames)
    {
        var grouped = rawNames
            .Where(n => n is not null)
            .GroupBy(n => n)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        return grouped?.Key ?? UnknownKey;
    }
}
