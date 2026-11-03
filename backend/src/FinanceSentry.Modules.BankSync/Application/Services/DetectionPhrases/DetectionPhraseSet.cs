namespace FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;

using FinanceSentry.Modules.BankSync.Domain;

/// <summary>
/// An immutable snapshot of the <c>detection_phrases</c> table, grouped by kind. The detection
/// classifiers take one as a parameter instead of holding their vocabulary in constants, so the
/// same code runs over whatever the table holds. <see cref="Seed"/> is the table as shipped, which
/// is what keeps behaviour identical until a row is edited.
/// </summary>
public sealed class DetectionPhraseSet
{
    private static readonly IReadOnlyList<string> NoPhrases = [];

    private readonly Dictionary<string, DetectionPhraseEntry[]> _byKind;

    /// <summary>The table as shipped: <see cref="DetectionPhraseSeedData.Entries"/>.</summary>
    public static DetectionPhraseSet Seed { get; } = new(DetectionPhraseSeedData.Entries);

    /// <summary>Builds a set from rows in the order they should be tried within their kind.</summary>
    public DetectionPhraseSet(IEnumerable<DetectionPhraseEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _byKind = entries
            .Where(e => !string.IsNullOrEmpty(e.Phrase))
            .Select(e => DetectionPhraseKinds.LowercaseKinds.Contains(e.Kind)
                ? e with { Phrase = e.Phrase.ToLowerInvariant() }
                : e)
            .GroupBy(e => e.Kind, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    }

    /// <summary>
    /// The phrases of a kind that apply to a transaction, in row order. A row with a condition
    /// applies only when the matching signal is passed and equal; a signal the caller does not have
    /// (<c>null</c>) therefore never satisfies one.
    /// </summary>
    public IEnumerable<string> Phrases(string kind, int? mcc = null, string? category = null)
    {
        if (!_byKind.TryGetValue(kind, out var entries))
            return NoPhrases;

        return Applicable(entries, mcc, category);
    }

    private static IEnumerable<string> Applicable(DetectionPhraseEntry[] entries, int? mcc, string? category)
    {
        foreach (var entry in entries)
        {
            if (entry.MccCondition is { } wantedMcc && wantedMcc != mcc) continue;
            if (entry.CategoryCondition is { } wantedCategory
                && !string.Equals(wantedCategory, category, StringComparison.Ordinal)) continue;
            yield return entry.Phrase;
        }
    }
}
