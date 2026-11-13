namespace FinanceSentry.Modules.Research.Domain.Coverage;

using FinanceSentry.Core.Auth;

/// <summary>Where a <see cref="CoverageEvent"/> stands after its review.</summary>
public enum CoverageEventDisposition
{
    /// <summary>Recorded; the review has not finished (it is retried by nothing, the row only says it never completed).</summary>
    Pending,

    /// <summary>Reviewed with the coverage rules; the name stays covered.</summary>
    Kept,

    /// <summary>Reviewed with the coverage rules; a remove rule fired and the name left coverage.</summary>
    Removed,
}

/// <summary>
/// One deterministic hit (a filing, an earnings date, a news cluster) on a covered name and what the pipeline did about
/// it: the discovery-funnel "events on coverage names" stage. A row is the once-only identity of the hit
/// (<see cref="Source"/> and <see cref="Reference"/> per user and ticker, the same identity the review-due notice uses), so
/// a detector re-reading the same event is a no-op, and the pulse reads the rows since its last run as the disposition
/// digest instead of being woken per event. No model is consulted anywhere on this path.
/// </summary>
public class CoverageEvent : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>Upper-case ticker of the covered name.</summary>
    public string Ticker { get; set; } = string.Empty;

    /// <summary>What kind of hit: a <c>ReviewDueSource</c> value (<c>filing</c>, <c>earnings</c>, <c>news</c>, ...).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Stable identity of the event behind the hit (accession number, ticker and date, ticker and day).</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>The hit in words ("10-Q filed 2026-11-12").</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>When the pipeline detected the hit; the instant the member's <c>LastEventAt</c> moved to.</summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    public CoverageEventDisposition Disposition { get; set; } = CoverageEventDisposition.Pending;

    /// <summary>When the review finished; null while <see cref="CoverageEventDisposition.Pending"/>.</summary>
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>True when the review re-ran the candidate score; false when the name has no Active candidate or the rescore failed.</summary>
    public bool Rescored { get; set; }

    /// <summary>The review-due notices the hit raised (<c>CandidateReviewDue</c>, <c>ThesisReviewDue</c>, comma separated); null when it maps to no thesis or Active candidate.</summary>
    public string? ReviewNotice { get; set; }

    /// <summary>The latest verdict word the coverage rules read for the name after the review; null when it has none.</summary>
    public string? Verdict { get; set; }

    /// <summary>The rule that removed the name (<see cref="CoverageRuleIds"/>); null when it was kept.</summary>
    public string? Rule { get; set; }

    /// <summary>The review in words: the rule's facts on a removal, why a rescore did not run or failed.</summary>
    public string Detail { get; set; } = string.Empty;
}
