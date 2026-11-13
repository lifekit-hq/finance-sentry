namespace FinanceSentry.Modules.Research.Domain.Coverage;

using System.Globalization;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain.Opportunity;

/// <summary>
/// The coverage add and remove rules (discovery funnel stage 1): pure and deterministic, no read, no model call.
/// Held and watchlisted names are <i>pinned</i>: always members, never removed. The rest earn a place by a verdict
/// history (Buy or StrongBuy on two days inside the window) or a Ledger nomination with a decision note, and lose
/// it by two consecutive Avoid verdicts, an expired candidate, or <c>NoEventDays</c> without an event and no dated
/// catalyst ahead. A name carrying an active thesis is protected from removal like a held one: its thesis needs it
/// covered.
/// </summary>
public static class CoverageRules
{
    public const string BuyVerdict = "Buy";
    public const string StrongBuyVerdict = "StrongBuy";
    public const string AvoidVerdict = "Avoid";

    /// <summary>Whether <paramref name="facts"/> earns an add, and by which rule; null when no add rule fires.</summary>
    /// <remarks>
    /// A ranked add (a candidate's verdicts or nomination) is withheld when a remove rule already matches the
    /// same facts, so a name is never added one night and removed the next on the evidence that added it.
    /// </remarks>
    public static CoverageAddDecision? EvaluateAdd(CoverageTickerFacts facts, CoverageOptions options, DateTimeOffset asOf)
    {
        if (facts.Held)
        {
            return new CoverageAddDecision(CoverageRuleIds.Held, CoverageReason.Held, true, "Held in the book.");
        }

        if (facts.Watchlisted)
        {
            return new CoverageAddDecision(CoverageRuleIds.Watchlist, CoverageReason.Watchlist, true, "On the watchlist.");
        }

        var ranked = EvaluateRankedAdd(facts, options, asOf);
        if (ranked is null)
        {
            return null;
        }

        // The remove rules judge the name as a member of its own: a new member's idle clock starts now.
        return EvaluateRemove(facts, options, asOf, memberSince: asOf) is null ? ranked : null;
    }

    /// <summary>The ranked add rule the facts satisfy, ignoring the remove rules (for the explanation).</summary>
    public static CoverageAddDecision? EvaluateRankedAdd(CoverageTickerFacts facts, CoverageOptions options, DateTimeOffset asOf)
    {
        var active = facts.ActiveCandidate;
        if (active is null)
        {
            return null;
        }

        if (NominationStands(facts, active))
        {
            return new CoverageAddDecision(
                CoverageRuleIds.LedgerNomination, CoverageReason.Ledger, false,
                "Ledger nominated the name with a decision note.");
        }

        var buyDays = BuyDaysInWindow(facts, options, asOf);
        if (active.Source is CandidateSource.Scan or CandidateSource.Ledger && buyDays >= options.BuyObservationsRequired)
        {
            return new CoverageAddDecision(
                CoverageRuleIds.BuyTwice, ReasonFor(active.Source), false,
                string.Create(CultureInfo.InvariantCulture,
                    $"Scored Buy or StrongBuy on {buyDays} days within {options.BuyWindowDays} days ({active.Source} candidate)."));
        }

        return null;
    }

    /// <summary>
    /// The removal cutoff of every ranked add rule: a name removed at <see cref="CoverageTickerFacts.LastRemovedAt"/>
    /// rejoins only on evidence created after that removal. A nomination is a candidate created at one moment, so it
    /// stands only when that candidate postdates the removal; Buy readings are cut off the same way in
    /// <see cref="BuyDaysInWindow"/>. The held and watchlist rules need no cutoff: they read current state and
    /// protect the name from every remove rule, so no removal can have overruled them.
    /// </summary>
    public static bool NominationStands(CoverageTickerFacts facts, CoverageCandidateFacts active)
        => active.Source == CandidateSource.Ledger
            && active.HasNominationNote
            && (facts.LastRemovedAt is not { } removedAt || active.CreatedAt > removedAt);

    /// <summary>
    /// Distinct days inside the window on which the verdict read Buy or StrongBuy, counting only readings after the
    /// day of the name's last removal: evidence a removal already overruled never earns the name back in.
    /// </summary>
    public static int BuyDaysInWindow(CoverageTickerFacts facts, CoverageOptions options, DateTimeOffset asOf)
    {
        var from = DateOnly.FromDateTime(asOf.UtcDateTime).AddDays(-options.BuyWindowDays);
        var removedOn = facts.LastRemovedAt is { } removedAt ? DateOnly.FromDateTime(removedAt.UtcDateTime) : (DateOnly?)null;
        return facts.Verdicts
            .Where(v => v.Day >= from && (removedOn is null || v.Day > removedOn) && v.Verdict is BuyVerdict or StrongBuyVerdict)
            .Select(v => v.Day)
            .Distinct()
            .Count();
    }

    /// <summary>
    /// The remove rule that takes an unprotected member out, in order: two consecutive Avoid verdicts, an expired
    /// candidate, the idle clock; null when none fires (or the name is protected).
    /// </summary>
    /// <param name="memberSince">When the name joined (<see cref="CoverageMember.AddedAt"/>): the idle clock's start when no event is known.</param>
    public static CoverageRemoveDecision? EvaluateRemove(
        CoverageTickerFacts facts, CoverageOptions options, DateTimeOffset asOf, DateTimeOffset memberSince)
    {
        if (facts.Protected)
        {
            return null;
        }

        if (AvoidStreak(facts) >= options.AvoidObservationsRequired)
        {
            return new CoverageRemoveDecision(
                CoverageRuleIds.AvoidTwice,
                string.Create(CultureInfo.InvariantCulture,
                    $"The last {options.AvoidObservationsRequired} verdict readings were Avoid."));
        }

        if (CandidateExpired(facts) is { } expired)
        {
            return new CoverageRemoveDecision(
                CoverageRuleIds.CandidateExpired,
                string.Create(CultureInfo.InvariantCulture, $"The candidate created {expired.CreatedAt:yyyy-MM-dd} expired."));
        }

        if (NoEventDue(facts, options, asOf, memberSince))
        {
            return new CoverageRemoveDecision(
                CoverageRuleIds.NoEvent,
                string.Create(CultureInfo.InvariantCulture,
                    $"No event since {IdleSince(facts, memberSince):yyyy-MM-dd} ({options.NoEventDays} days) and no dated catalyst in the next {options.CatalystLookaheadDays} days."));
        }

        return null;
    }

    /// <summary>
    /// When the no-event rule would remove the name: null for a protected name; otherwise the idle clock plus
    /// <c>NoEventDays</c>, pushed out to the next dated catalyst when that is later.
    /// </summary>
    public static DateTimeOffset? RemoveAfter(CoverageTickerFacts facts, CoverageOptions options, DateTimeOffset memberSince)
    {
        if (facts.Protected)
        {
            return null;
        }

        var after = IdleSince(facts, memberSince).AddDays(options.NoEventDays);
        if (facts.NextCatalyst is { } catalyst)
        {
            var catalystAt = new DateTimeOffset(catalyst.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            if (catalystAt > after)
            {
                after = catalystAt;
            }
        }

        return after;
    }

    /// <summary>How many of the latest verdict readings, counting back from the newest, are Avoid.</summary>
    public static int AvoidStreak(CoverageTickerFacts facts)
    {
        var streak = 0;
        foreach (var reading in facts.Verdicts.OrderByDescending(v => v.Day))
        {
            if (reading.Verdict != AvoidVerdict)
            {
                break;
            }

            streak++;
        }

        return streak;
    }

    /// <summary>The cap-binding rank: the scorecard sub-score mean less the D1 correlation penalty. Higher wins a slot.</summary>
    public static int Rank(CoverageTickerFacts facts) => (facts.RankBase ?? 0) - facts.CorrelationPenalty;

    /// <summary>The expired candidate that ended the name's candidacy: the newest candidate is Expired and none is Active; otherwise null.</summary>
    public static CoverageCandidateFacts? CandidateExpired(CoverageTickerFacts facts)
        => facts.ActiveCandidate is null && facts.LatestCandidate is { Status: CandidateStatus.Expired } expired ? expired : null;

    /// <summary>
    /// True when the idle clock has run out and a catalyst lookup positively found nothing ahead. A lookup that
    /// failed (<see cref="CoverageTickerFacts.CatalystKnown"/> false) never counts as "nothing ahead".
    /// </summary>
    public static bool NoEventDue(CoverageTickerFacts facts, CoverageOptions options, DateTimeOffset asOf, DateTimeOffset memberSince)
        => asOf >= IdleSince(facts, memberSince).AddDays(options.NoEventDays) && facts.CatalystKnown && facts.NextCatalyst is null;

    /// <summary>
    /// When the idle clock started: the later of the latest known event and when the name joined (joining coverage
    /// resets the clock, so a name is not added on one night and removed on the next for an old event).
    /// </summary>
    public static DateTimeOffset IdleSince(CoverageTickerFacts facts, DateTimeOffset memberSince)
        => facts.LastEventAt is { } last && last > memberSince ? last : memberSince;

    private static CoverageReason ReasonFor(CandidateSource source)
        => source == CandidateSource.Ledger ? CoverageReason.Ledger : CoverageReason.Screen;
}
