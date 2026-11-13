namespace FinanceSentry.Modules.Research.Domain.Coverage;

using FinanceSentry.Core.Auth;

/// <summary>
/// One company in the user's coverage universe, "our own index": a capped list (<c>Coverage:MaxMembers</c>)
/// the pipeline adds names to and removes names from by deterministic rules (<see cref="CoverageRules"/>), with
/// no model call and no owner confirmation. Held and watchlisted names are always members; the rest earn their
/// place. A member row exists only while the name is covered: leaving deletes the row, and the
/// <see cref="CoverageJournalEntry"/> rows keep the history.
/// </summary>
public class CoverageMember : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>Upper-case ticker; unique per user.</summary>
    public string Ticker { get; set; } = string.Empty;

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Why the name was added; kept for history even after a held name is sold.</summary>
    public CoverageReason AddedReason { get; set; }

    /// <summary>The latest dated thing that happened to the name (a rescore, a thesis or candidate event); null until one is seen.</summary>
    public DateTimeOffset? LastEventAt { get; set; }

    /// <summary>The newest <see cref="CandidateScore"/> for the ticker; null when it has never been scored.</summary>
    public Guid? LastScoreId { get; set; }

    /// <summary>
    /// 63-day return correlation to the book (<c>IReturnCorrelationReader</c>, rule D1); null when the bars
    /// cannot support it (the explanation says why) or before the first evaluation.
    /// </summary>
    public decimal? CorrelationToBook { get; set; }

    /// <summary>
    /// When the no-event remove rule would take the name out, if nothing happens first; null for a name the
    /// remove rule cannot touch (held, watchlisted, or carrying an active thesis).
    /// </summary>
    public DateTimeOffset? RemoveAfter { get; set; }
}
