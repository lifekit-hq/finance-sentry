namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Paper picks (#986): every opportunity candidate, in any status, is a pick graded against the S&amp;P 500 from its
/// price-stamped <c>Created</c> event to a price stamped at the same horizon a decided proposal is scored over
/// (<see cref="ScoreRecommendationOutcomesCommandHandler.HorizonDays"/>). A promoted, rejected or expired pick is still
/// graded, so a rejection reads as the counterfactual. The horizon point is the first <c>Expired</c> or <c>Snapshot</c>
/// event at or after the horizon and inside <see cref="GraceDays"/> of it; a pick whose horizon passed without one is
/// left ungraded rather than priced at a later date.
/// </summary>
public static class PaperPickHorizon
{
    public const int HorizonDays = ScoreRecommendationOutcomesCommandHandler.HorizonDays;

    /// <summary>How late a horizon stamp may land and still grade the pick: a missed run or a closed market.</summary>
    public const int GraceDays = 7;

    public static DateTimeOffset Due(OpportunityCandidate candidate) => candidate.CreatedAt.AddDays(HorizonDays);

    public static DateTimeOffset Deadline(OpportunityCandidate candidate) => Due(candidate).AddDays(GraceDays);

    /// <summary>The pick's entry: its one <c>Created</c> event, or null for a candidate recorded before the journal existed.</summary>
    public static ThesisEvent? Entry(IEnumerable<ThesisEvent> events)
        => events.Where(e => e.EventType == ThesisEventType.Created).OrderBy(e => e.Timestamp).FirstOrDefault();

    /// <summary>The pick's horizon point, or null when none has been stamped inside the window.</summary>
    public static ThesisEvent? HorizonPoint(OpportunityCandidate candidate, IEnumerable<ThesisEvent> events)
    {
        var due = Due(candidate);
        var deadline = Deadline(candidate);
        return events
            .Where(e => e.EventType is ThesisEventType.Expired or ThesisEventType.Snapshot)
            .Where(e => e.Timestamp >= due && e.Timestamp <= deadline)
            .OrderBy(e => e.Timestamp)
            .FirstOrDefault();
    }
}
