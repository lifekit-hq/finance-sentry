namespace FinanceSentry.Modules.Research.Domain.Revisions;

using FinanceSentry.Core.Auth;

/// <summary>What a proposal asks the owner to do to a thesis or candidate.</summary>
public enum ThesisRevisionKind
{
    /// <summary>Promote an Active candidate into a monitored thesis (runs the risk gate on confirm).</summary>
    Promote,

    /// <summary>Change a thesis's narrative: text, key data points, catalysts, narrative falsifiers, entry price.</summary>
    NarrativeUpdate,

    /// <summary>Change a thesis's invalidation triggers.</summary>
    TriggerChange,

    /// <summary>Retire a thesis (the same removal as deleting it, journalled as Closed).</summary>
    Close,
}

public enum ThesisRevisionStatus
{
    Pending,
    Confirmed,
    Rejected,
    Expired,
    Superseded,
}

/// <summary>
/// The content a proposal would write. Null leaves the field as it is on a revision, or lets the promotion default
/// stand (scorecard-built text and data points, the deterministic trigger prefill); an empty list clears.
/// </summary>
public sealed record ThesisRevisionContent(
    string? ThesisText = null,
    IReadOnlyList<ThesisDataPoint>? KeyDataPoints = null,
    IReadOnlyList<ThesisCatalyst>? Catalysts = null,
    IReadOnlyList<ThesisInvalidationTrigger>? InvalidationTriggers = null,
    IReadOnlyList<string>? NarrativeFalsifiers = null,
    decimal? EntryPrice = null)
{
    public bool IsEmpty =>
        ThesisText is null && KeyDataPoints is null && Catalysts is null && InvalidationTriggers is null
        && NarrativeFalsifiers is null && EntryPrice is null;
}

/// <summary>
/// One change to a thesis (or one promotion) put to the owner as a pending artifact. Ledger proposes through its service
/// token; only the owner's confirm writes the thesis, and it does so through the same commands the owner's own edits
/// use, so a promotion still runs the risk gate. A confirm is therefore the single step that writes. At most one
/// <see cref="ThesisRevisionStatus.Pending"/> row exists per (UserId, SubjectId); a newer proposal supersedes it. Rows are
/// never deleted: every proposal ends confirmed, rejected, expired or superseded, with the reason kept.
/// </summary>
public class ThesisRevision : IUserOwned
{
    public const int MaxRationaleLength = 2000;
    public const int MaxDecisionNoteLength = 2000;
    public const int DefaultExpiryDays = 14;
    public const int MaxExpiryDays = 30;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ThesisRevisionKind Kind { get; set; }
    public ThesisRevisionStatus Status { get; set; } = ThesisRevisionStatus.Pending;

    /// <summary>Who put it forward. Confirming is always the owner's act, whoever proposed.</summary>
    public ThesisEventOrigin ProposedBy { get; set; }

    /// <summary>A candidate (<see cref="ThesisRevisionKind.Promote"/>) or a thesis (every other kind).</summary>
    public ThesisSubjectType SubjectType { get; set; }

    public Guid SubjectId { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public ThesisRevisionContent Proposed { get; set; } = new();
    public string Rationale { get; set; } = string.Empty;
    public ThesisEvidenceRefs? Evidence { get; set; }

    /// <summary>Promote only: the position size the risk gate checks on confirm (0 = no sizing).</summary>
    public decimal ProposedUsd { get; set; }

    /// <summary>Promote only: a tracking-only promotion, which the gate does not check against real cash.</summary>
    public bool Paper { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public Guid? SupersededById { get; set; }

    /// <summary>The thesis a confirm created or revised.</summary>
    public Guid? ResultThesisId { get; set; }
}
