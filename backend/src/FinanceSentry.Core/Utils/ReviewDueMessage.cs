namespace FinanceSentry.Core.Utils;

using System.Globalization;
using System.Text;
using FinanceSentry.Core.Interfaces;

/// <summary>
/// The text form of a <see cref="ReviewDue"/>, in the one place the alerts and companion modules agree on it. An alert has
/// no payload column, so the notice rides its message: a human headline, then a machine tail
/// <c>[review subject=thesis:&lt;id&gt; ticker=NVDA source=filing ref=&lt;ref&gt; evidence=a|b]</c>. Companion reads the tail back
/// (<see cref="TryParse"/>) to pick the delivery lane from the source and to hand Ledger the subject id and the evidence
/// references without a schema change. Values never contain whitespace, <c>[</c>, <c>]</c> or <c>|</c> (<see cref="Clean"/>),
/// so the tail parses unambiguously.
/// </summary>
public static class ReviewDueMessage
{
    /// <summary>The alert message column (<c>Alert.Message</c>) holds this many characters.</summary>
    public const int MaxMessageLength = 1000;

    private const string TailOpen = " [review ";
    private const char TailClose = ']';
    private const char EvidenceSeparator = '|';
    private const char Elision = '…';

    /// <summary>Evidence is dropped before the headline is cut below this many characters.</summary>
    private const int MinHeadlineRoom = 120;

    public static string SubjectKey(ReviewSubjectKind kind) => kind == ReviewSubjectKind.Thesis ? "thesis" : "candidate";

    /// <summary>The alert message: headline then tail, within <paramref name="maxLength"/> (evidence is dropped from the end, then the headline is cut).</summary>
    public static string Compose(ReviewDue review, int maxLength = MaxMessageLength)
    {
        var evidence = review.Evidence.Select(Clean).Where(e => e.Length > 0).ToList();
        while (true)
        {
            var tail = Tail(review, evidence);
            var room = maxLength - tail.Length;
            if (room >= Math.Min(review.Headline.Length, MinHeadlineRoom) || evidence.Count == 0)
            {
                return Truncate(review.Headline, Math.Max(room, 0)) + tail;
            }

            evidence.RemoveAt(evidence.Count - 1);
        }
    }

    /// <summary>
    /// The companion event summary: what Ledger triages from, capped at <paramref name="maxLength"/>. The subject, source
    /// and reference always survive; evidence references are kept while they fit, and the headline takes what is left.
    /// </summary>
    public static string Summarize(ReviewDue review, int maxLength)
    {
        var lead = $"{(review.Subject == ReviewSubjectKind.Thesis ? "Thesis" : "Candidate")} review due: {review.Ticker} ({review.Source})";
        var core = $" | subject={SubjectKey(review.Subject)}:{review.SubjectId:D} | source={review.Source} | ref={Clean(review.Reference)}";
        var summary = new StringBuilder(lead).Append(core);

        var evidence = review.Evidence.Select(Clean).Where(e => e.Length > 0).ToList();
        if (evidence.Count > 0)
        {
            const string evidenceLead = " | evidence=";
            var kept = new List<string>();
            var used = summary.Length + evidenceLead.Length;
            foreach (var item in evidence)
            {
                var cost = item.Length + (kept.Count > 0 ? 1 : 0);
                if (used + cost > maxLength)
                {
                    break;
                }

                kept.Add(item);
                used += cost;
            }

            if (kept.Count > 0)
            {
                summary.Append(evidenceLead).Append(string.Join(EvidenceSeparator, kept));
            }
        }

        const string headlineLead = " | ";
        var remaining = maxLength - summary.Length - headlineLead.Length;
        if (review.Headline.Length > 0 && remaining > 1)
        {
            summary.Append(headlineLead).Append(Truncate(review.Headline, remaining));
        }

        return summary.Length <= maxLength ? summary.ToString() : summary.ToString(0, maxLength);
    }

    /// <summary>Reads the tail back from an alert message; false for any other alert, or a tail that is not well formed.</summary>
    public static bool TryParse(string? message, out ReviewDue review)
    {
        review = null!;
        if (message is null)
        {
            return false;
        }

        var open = message.LastIndexOf(TailOpen, StringComparison.Ordinal);
        if (open < 0 || !message.EndsWith(TailClose))
        {
            return false;
        }

        var headline = message[..open];
        var body = message[(open + TailOpen.Length)..^1];
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in body.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                fields[part[..eq]] = part[(eq + 1)..];
            }
        }

        if (!fields.TryGetValue("subject", out var subject)
            || !fields.TryGetValue("ticker", out var ticker)
            || !fields.TryGetValue("source", out var source)
            || !fields.TryGetValue("ref", out var reference))
        {
            return false;
        }

        var colon = subject.IndexOf(':');
        if (colon < 0
            || !Guid.TryParse(subject[(colon + 1)..], out var subjectId)
            || !TryKind(subject[..colon], out var kind))
        {
            return false;
        }

        var evidence = fields.TryGetValue("evidence", out var raw)
            ? raw.Split(EvidenceSeparator, StringSplitOptions.RemoveEmptyEntries)
            : [];
        review = new ReviewDue(kind, subjectId, ticker, source, reference, evidence, headline);
        return true;
    }

    private static string Tail(ReviewDue review, List<string> evidence)
    {
        var tail = new StringBuilder(TailOpen)
            .Append(CultureInfo.InvariantCulture, $"subject={SubjectKey(review.Subject)}:{review.SubjectId:D}")
            .Append(" ticker=").Append(Clean(review.Ticker))
            .Append(" source=").Append(Clean(review.Source))
            .Append(" ref=").Append(Clean(review.Reference));
        if (evidence.Count > 0)
        {
            tail.Append(" evidence=").Append(string.Join(EvidenceSeparator, evidence));
        }

        return tail.Append(TailClose).ToString();
    }

    private static bool TryKind(string key, out ReviewSubjectKind kind)
    {
        switch (key)
        {
            case "thesis":
                kind = ReviewSubjectKind.Thesis;
                return true;
            case "candidate":
                kind = ReviewSubjectKind.Candidate;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    /// <summary>Keeps a value inside the tail's grammar: whitespace and the delimiters become <c>_</c>.</summary>
    private static string Clean(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            builder.Append(char.IsWhiteSpace(c) || c is '[' or ']' or EvidenceSeparator ? '_' : c);
        }

        return builder.ToString();
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : max <= 1 ? string.Empty : string.Concat(value.AsSpan(0, max - 1), Elision.ToString());
}
