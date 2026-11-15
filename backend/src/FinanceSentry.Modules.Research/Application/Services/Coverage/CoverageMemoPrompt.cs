namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.ThesisMonitor;

/// <summary>What the memo prompt is built from: facts the pipeline already holds, nothing fetched for the model.</summary>
public sealed record CoverageMemoEvidence(
    CoverageJournalEntry Entry,
    CoverageMember? Member,
    ScorecardResult? Scorecard,
    IReadOnlyList<CoverageEvent> Events);

/// <summary>
/// The memo prompt (discovery funnel stage 6), pure: a fixed system prompt that fixes the output shape and the
/// recommend-only stance, and a user message of evidence the pipeline already stored (why the name was added, the
/// deterministic scorecard, the latest events). The model reads facts; it is asked for nothing it could only get by
/// browsing, and it is told to say what the evidence does not show.
/// </summary>
public static class CoverageMemoPrompt
{
    private const string TruncationNote = "\n[evidence cut here: the rest was left out to bound the prompt]";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string System { get; } = string.Create(CultureInfo.InvariantCulture, $$"""
        You write one research memo about one company that has just entered a personal coverage list. You advise a single
        private investor; nothing you write places, sizes or changes a position, and the memo is graded against the S&P 500
        before anyone acts on it. Use only the evidence in the user message. Where it does not show something, say so in
        "caveats" instead of filling it in from memory.

        Reply with one JSON object and nothing else, with exactly these fields:
        {
          "business": "what the company sells and to whom, in two or three sentences",
          "whyNow": "the catalyst or fundamentals change that put it on the list, from the evidence",
          "falsifiers": [ three to five things that would make the call wrong, each:
            {"kind": "metric", "statement": "...", "metric": "<one of: {{string.Join(", ", ThesisMetric.All.Order(StringComparer.Ordinal))}}>", "direction": "above|below", "threshold": <number>}
            or {"kind": "narrative", "statement": "..."} ],
          "crowding": "how stretched or crowded the name is, from the crowding facts",
          "correlationToBook": "how much it moves with the owner's book (DRAM and semiconductors dominate it) and what that means for the diversification goal",
          "verdict": "Buy | Watch | Avoid",
          "caveats": "what the evidence did not show"
        }
        Fractions are fractions (0.12 is 12%). Include at least one "metric" falsifier when the evidence supports one. "Buy"
        means worth sizing if the paper record agrees, "Watch" means keep covering, "Avoid" means the evidence argues against.
        """);

    /// <summary>The user message for <paramref name="evidence"/>, cut to <paramref name="maxChars"/> characters.</summary>
    public static string User(CoverageMemoEvidence evidence, int maxChars)
    {
        var entry = evidence.Entry;
        var facts = new
        {
            ticker = entry.Ticker,
            entry = new
            {
                at = entry.At,
                rule = entry.Rule,
                reason = entry.Reason.ToString(),
                detail = entry.Detail,
                rank = entry.RankScore,
                correlationToBook = entry.CorrelationToBook,
            },
            member = evidence.Member is null
                ? null
                : new { evidence.Member.AddedAt, evidence.Member.LastEventAt, evidence.Member.CorrelationToBook },
            scorecard = evidence.Scorecard is null
                ? null
                : evidence.Scorecard with { Verdict = evidence.Scorecard.Verdict with { Rules = [] }, LookThrough = null },
            events = evidence.Events.Select(e => new
            {
                at = e.OccurredAt,
                source = e.Source,
                summary = e.Summary,
                disposition = e.Disposition.ToString(),
            }),
        };

        var text = new StringBuilder("Evidence (JSON):\n").Append(JsonSerializer.Serialize(facts, Json)).ToString();
        return text.Length <= maxChars ? text : string.Concat(text.AsSpan(0, maxChars), TruncationNote);
    }
}
