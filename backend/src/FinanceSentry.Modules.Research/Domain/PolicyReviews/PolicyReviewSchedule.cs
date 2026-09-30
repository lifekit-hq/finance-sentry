namespace FinanceSentry.Modules.Research.Domain.PolicyReviews;

/// <summary>
/// When the next policy review is due (#696), derived from the statement alone: the cadence comes
/// from the current version, and the anchor is the most recent review recorded on any version — a
/// new version of the statement does not restart the clock. A statement never reviewed is anchored
/// on its first authoring, so its first review falls one cadence after it was written.
/// </summary>
public static class PolicyReviewSchedule
{
    /// <summary>
    /// How late a due review may open before it counts as missed. The review job runs daily, so a
    /// review due mid-day opens at the next run; beyond two days at least one scheduled run failed
    /// to open it and the lapse itself is reported.
    /// </summary>
    public static readonly TimeSpan MissedGrace = TimeSpan.FromDays(2);

    public static PolicyReviewStatus Evaluate(IReadOnlyList<InvestmentPolicyStatement> versions, DateTimeOffset now)
    {
        if (versions.Count == 0)
            throw new ArgumentException("A review schedule needs at least one policy statement version.", nameof(versions));

        var current = versions.FirstOrDefault(v => v.IsCurrent) ?? versions.MaxBy(v => v.Version)!;
        var recognised = PolicyReviewCadence.TryGetMonths(current.ReviewCadence, out var months);

        var lastReviewedAt = versions.Max(v => v.LastReviewedAt);
        var anchor = lastReviewedAt ?? versions.Min(v => v.CreatedAt);
        var dueAt = anchor.AddMonths(months);

        var isDue = now >= dueAt;
        var overdue = isDue ? now - dueAt : TimeSpan.Zero;

        return new PolicyReviewStatus(
            current.ReviewCadence,
            months,
            recognised,
            lastReviewedAt,
            dueAt,
            isDue,
            (int)overdue.TotalDays,
            overdue > MissedGrace);
    }
}

/// <summary>Where the policy review stands: when it is due, and whether it is overdue or missed.</summary>
public sealed record PolicyReviewStatus(
    string Cadence,
    int CadenceMonths,
    bool CadenceRecognised,
    DateTimeOffset? LastReviewedAt,
    DateTimeOffset DueAt,
    bool IsDue,
    int DaysOverdue,
    bool IsMissed);
