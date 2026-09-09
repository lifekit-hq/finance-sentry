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
    /// The mobile-top-up key this class emits, recognised on the way back in so re-deriving a
    /// key is a no-op (see <see cref="NormalizeDetectionKey"/>). Matched case-insensitively and
    /// returned lowercased: a statement line that already reads like the key must land on it
    /// too, or the same charge would key two ways depending on how the bank spelled it.
    /// </summary>
    private static readonly Regex MobileTopUpKeyPattern =
        new(@"^mobile top-up \d{4}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The merchant key recurring-charge detection groups by, and the key stored on
    /// <c>DetectedSubscription.MerchantNameNormalized</c>. Anything that needs to ask "is this
    /// transaction one of the user's detected commitments?" must derive its key here — a plain
    /// <see cref="Normalize"/> of the merchant name would miss the description fallback and the
    /// mobile top-up collapsing, and so would systematically under-match.
    /// <para>
    /// <b>Idempotent by contract</b>: <c>f(f(x)) == f(x)</c>. Keys derived here are persisted and
    /// handed back out (a pin listing, a detected subscription), so callers re-derive them
    /// routinely — a caller unpinning by the key a listing just advertised is re-deriving. Any
    /// clause whose output re-normalizes to something else forces every such caller to carry a
    /// second lookup path, so new clauses must recognise what they emit.
    /// </para>
    /// </summary>
    public static string NormalizeDetectionKey(string? merchantName, string? description)
    {
        var raw = merchantName ?? description;
        if (raw is not null)
        {
            var trimmed = raw.Trim();

            // Already the key the clause below emits. Without this, `mobile top-up 0057` reads
            // as a merchant with statement digits glued on and collapses to `mobile top-up`.
            if (MobileTopUpKeyPattern.IsMatch(trimmed))
                return trimmed.ToLowerInvariant();

            // Mobile top-ups carry the phone number in the description
            // ("*MOBI TOP-UP 0857860057"), which both fragments the merchant key and trips the
            // generic top-up blocklist — collapse them to a stable per-number key instead so a
            // monthly top-up is tracked like any other recurring cost.
            var mobi = MobiTopUpPattern.Match(trimmed);
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

        // Strip repeatedly rather than once: one pass can uncover work for the next — the domain
        // suffix in "*netflix.com 12" only reaches the end of the string after the trailing
        // statement digits go — and a key this method emitted has to normalize to itself.
        // Terminates because no pass ever lengthens the string and the one length-preserving
        // step (collapsing a whitespace run to a single space) is idempotent.
        string previous;
        do
        {
            previous = result;
            result = StripStatementNoise(result);
        }
        while (result != previous);

        if (string.IsNullOrWhiteSpace(result))
            return UnknownKey;

        foreach (var (keyword, canonical) in BrandAliases)
        {
            if (result.Contains(keyword, StringComparison.Ordinal))
                return canonical;
        }

        return result;
    }

    /// <summary>
    /// One pass of the noise a statement line wraps a merchant name in: the PayPal prefix, a
    /// domain suffix, leading punctuation, trailing card/reference digits, doubled spaces.
    /// </summary>
    private static string StripStatementNoise(string value)
    {
        var result = value;

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

        return CollapseSpacesPattern.Replace(result, " ").Trim();
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
