namespace FinanceSentry.Modules.Research.Application.Services;

using System.Text.RegularExpressions;

/// <summary>
/// Whole-word geopolitics/policy term matching (N2, ledger-heartbeat design), shared by
/// <see cref="Infrastructure.Jobs.GeopoliticsSourceSeedJob"/> (registers a thesis-owned source the
/// first time a thesis's text matches) and <see cref="Infrastructure.Jobs.ThesisSourceRetirementJob"/>
/// (retires one once its thesis's text no longer does). One term list and one matching rule for both
/// directions, so a source is never left in service for terms registration itself would no longer
/// recognise, and never retired for terms registration still would.
/// </summary>
public static class GeopoliticsTermMatcher
{
    private const int MaxTermsPerQuery = 3;

    private const string GoogleNewsRssPrefix = "https://news.google.com/rss/search?q=";

    /// <summary>Terms report §5.3, N2 examples: Ukraine ceasefire, sanctions, export controls, SEC crypto rulings, stablecoin bill.</summary>
    private static readonly string[] Terms =
    [
        "sanction", "tariff", "export control", "ceasefire", "embargo",
        "war", "conflict", "ruling", "regulation", "regulatory", "SEC", "stablecoin", "bill",
    ];

    private static readonly Regex[] Patterns =
        [.. Terms.Select(term => new Regex(
            $@"\b{Regex.Escape(term)}s?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))];

    /// <summary>Terms whole-word-matched (optionally pluralised) in <paramref name="thesisText"/>, capped to <see cref="MaxTermsPerQuery"/>.</summary>
    public static List<string> MatchTerms(string thesisText)
        => [.. Terms
            .Where((_, i) => Patterns[i].IsMatch(thesisText))
            .Take(MaxTermsPerQuery)];

    /// <summary>
    /// The Google News RSS source URL the seed registers for a thesis right now, or null when its text
    /// matches no term. The thesis id rides in the fragment (never sent to Google) so each thesis owns its URL.
    /// </summary>
    public static string? SourceUrlFor(Guid thesisId, string ticker, string thesisText)
    {
        var terms = MatchTerms(thesisText);
        if (terms.Count == 0)
        {
            return null;
        }

        var query = $"{ticker} ({string.Join(" OR ", terms)})";
        return $"{GoogleNewsRssPrefix}{Uri.EscapeDataString(query)}&hl=en-US&gl=US&ceid=US:en{ThesisMarker(thesisId)}";
    }

    /// <summary>Whether <paramref name="url"/> is a seed-registered source for <paramref name="thesisId"/> (not a user-registered one).</summary>
    public static bool IsSeededSourceUrl(string url, Guid thesisId)
        => url.StartsWith(GoogleNewsRssPrefix, StringComparison.Ordinal)
            && url.EndsWith(ThesisMarker(thesisId), StringComparison.Ordinal);

    private static string ThesisMarker(Guid thesisId) => $"#thesis={thesisId:N}";
}
