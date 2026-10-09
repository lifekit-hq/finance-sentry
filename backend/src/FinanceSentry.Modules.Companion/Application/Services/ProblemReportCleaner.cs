namespace FinanceSentry.Modules.Companion.Application.Services;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// Server-side cleaning of everything a problem report carries from the client, before it is stored: the text
/// reaches an agent's prompt, so nothing in it may smuggle structure, account numbers or amounts through.
/// </summary>
public static partial class ProblemReportCleaner
{
    public const string NumberRemoved = "[number removed]";

    public const string Unknown = "unknown";

    private static readonly string[] OsFamilies = ["iOS", "iPadOS", "Android", "Windows", "macOS", "Linux", "ChromeOS"];

    private static readonly string[] BrowserFamilies = ["Chrome", "Safari", "Firefox", "Edge", "Opera", "Samsung Internet", "Brave"];

    private const string CurrencySigns = "$€£¥₴₽₹";

    private const string CurrencyCodes = "USD|EUR|GBP|UAH|CHF|PLN|CAD|AUD|JPY";

    // The digits of one number: grouping marks and single spaces (space, NBSP, narrow NBSP) between digits belong to it.
    private const string Number = @"\d(?:[ \u00A0\u202F](?=\d)|[\d.,'])*";

    // A sign or ISO code on either side of a number: $1,200.50 · €5k · 950 EUR · CHF 950.00 · 12 345 678 ₴
    [GeneratedRegex($@"(?:[{CurrencySigns}]|\b(?:{CurrencyCodes}))\s*{Number}[kKmM]?|{Number}[kKmM]?\s*(?:[{CurrencySigns}]|(?:{CurrencyCodes})\b)", RegexOptions.IgnoreCase)]
    private static partial Regex Amount();

    [GeneratedRegex(@"\d(?:[ \u00A0\u202F]?\d){5,}")]
    private static partial Regex LongDigitRun();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Guid();

    [GeneratedRegex(@"[^A-Za-z0-9/:_.\-~*\[\]]")]
    private static partial Regex RouteDisallowed();

    [GeneratedRegex(@"^[0-9A-Za-z][0-9A-Za-z.+\-]*$")]
    private static partial Regex VersionShape();

    [GeneratedRegex(@"[^A-Za-z0-9._:\-]")]
    private static partial Regex TokenDisallowed();

    /// <summary>
    /// Free text: control, format and line/paragraph-separator characters removed (a tab becomes a space, line breaks
    /// are kept as <c>\n</c> so a multi-line message stays readable), amounts with a currency sign and runs of six or
    /// more digits replaced by <see cref="NumberRemoved"/>, cut to <see cref="ProblemReportLimits.TextMaxLength"/>.
    /// Null when nothing is left.
    /// </summary>
    public static string? Text(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var input = Truncate(raw, ProblemReportLimits.TextMaxLength);
        var sb = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '\r')
            {
                if (i + 1 < input.Length && input[i + 1] == '\n')
                    continue;
                sb.Append('\n');
            }
            else if (c == '\n')
            {
                sb.Append('\n');
            }
            else if (c == '\t')
            {
                sb.Append(' ');
            }
            else if (!IsStripped(c))
            {
                sb.Append(c);
            }
        }

        var cleaned = LongDigitRun().Replace(Amount().Replace(sb.ToString(), NumberRemoved), NumberRemoved).Trim();
        cleaned = Truncate(cleaned, ProblemReportLimits.TextMaxLength).TrimEnd();
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>
    /// The router's route pattern (<c>/accounts/:id</c>). Anything after <c>?</c> or <c>#</c> is dropped, characters
    /// outside a path alphabet are removed, and GUIDs and long digit runs a client left in become <c>:id</c>.
    /// <see cref="Unknown"/> when it is not a path.
    /// </summary>
    public static string RoutePattern(string? raw)
    {
        var path = raw ?? string.Empty;
        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
            path = path[..cut];

        path = RouteDisallowed().Replace(path, string.Empty);
        path = LongDigitRun().Replace(Guid().Replace(path, ":id"), ":id");
        return path.StartsWith('/') ? Truncate(path, ProblemReportLimits.RouteMaxLength) : Unknown;
    }

    public static string AppVersion(string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        return value.Length is > 0 and <= ProblemReportLimits.VersionMaxLength && VersionShape().IsMatch(value) ? value : Unknown;
    }

    /// <summary>
    /// Operating system and browser family joined, for example <c>iOS Safari</c>. Each part has to be one of the known
    /// families (any case, canonical spelling kept); anything else is dropped, so free text never reaches the note header.
    /// </summary>
    public static string Client(string? os, string? browser)
    {
        var parts = new[] { Family(os, OsFamilies), Family(browser, BrowserFamilies) }.Where(p => p != Unknown).ToArray();
        return parts.Length == 0 ? Unknown : string.Join(' ', parts);
    }

    /// <summary>An identifier that is printed into the note: restricted to a safe alphabet and length.</summary>
    public static string Token(string? raw, int maxLength)
    {
        var value = TokenDisallowed().Replace(raw ?? string.Empty, string.Empty);
        return value.Length == 0 ? Unknown : Truncate(value, maxLength);
    }

    private static string Family(string? raw, string[] families) =>
        families.FirstOrDefault(f => string.Equals(f, raw?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Unknown;

    private static bool IsStripped(char c) => char.GetUnicodeCategory(c) is
        UnicodeCategory.Control
        or UnicodeCategory.Format
        or UnicodeCategory.LineSeparator
        or UnicodeCategory.ParagraphSeparator;

    // Never leaves half of a surrogate pair behind.
    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;

        var length = char.IsHighSurrogate(value[max - 1]) ? max - 1 : max;
        return value[..length];
    }
}
