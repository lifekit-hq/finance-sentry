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
}
