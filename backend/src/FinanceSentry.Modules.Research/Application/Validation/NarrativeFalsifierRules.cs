namespace FinanceSentry.Modules.Research.Application.Validation;

using FinanceSentry.Modules.Research.Domain.Exceptions;

/// <summary>
/// Narrative falsifiers are free text kept beside the thesis, outside the closed metric vocabulary
/// (<see cref="ThesisTriggerVocabulary"/> stays closed). The rules only keep the list tidy and bounded: entries are
/// trimmed, blanks dropped, repeats (ignoring case) collapsed, and an over-long entry or list is refused rather than
/// cut, since a silently shortened kill-switch would mean something else.
/// </summary>
public static class NarrativeFalsifierRules
{
    public const int MaxCount = 20;
    public const int MaxLength = 500;

    public static IReadOnlyList<string> Normalize(IReadOnlyList<string> falsifiers)
    {
        var kept = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in falsifiers)
        {
            var text = raw?.Trim();
            if (string.IsNullOrEmpty(text) || !seen.Add(text))
            {
                continue;
            }

            if (text.Length > MaxLength)
            {
                throw new InvalidNarrativeFalsifierException(
                    $"A narrative falsifier is at most {MaxLength} characters; this one is {text.Length}.");
            }

            kept.Add(text);
        }

        if (kept.Count > MaxCount)
        {
            throw new InvalidNarrativeFalsifierException(
                $"A thesis carries at most {MaxCount} narrative falsifiers; this one has {kept.Count}.");
        }

        return kept;
    }
}
