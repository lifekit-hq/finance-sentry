namespace FinanceSentry.Modules.Research.Domain.Recommendations;

/// <summary>
/// The one definition of what a decided proposal earned over the S&amp;P 500 (#1069, Ledger scoreboard). The Decisions
/// page, Ledger's tools and the scoreboard all read these values, so no copy of the sign rule lives anywhere else.
/// All figures are percentage points of excess return: the name's return since the decision minus the S&amp;P 500's.
/// </summary>
public static class RecommendationValue
{
    /// <summary>The name's return since the decision minus the S&amp;P 500's; null until the outcome job has scored both.</summary>
    public static decimal? ExcessReturnPct(Recommendation r)
        => r.SubjectReturnPct is decimal s && r.BenchmarkReturnPct is decimal b ? s - b : null;

    /// <summary>
    /// What the owner's own decision earned over the S&amp;P 500: name minus S&amp;P 500 where exposure was kept or added
    /// (a confirmed Add or Hold, a rejected Trim or Exit), S&amp;P 500 minus name where it was reduced or declined (a
    /// confirmed Trim or Exit, a rejected Add). Null when unscored, when a confirmed order was never placed, and for a
    /// rejected Hold, whose direction is unknown.
    /// </summary>
    public static decimal? DecisionValuePct(Recommendation r)
        => KeptExposure(r) is bool kept && ExcessReturnPct(r) is decimal excess ? (kept ? excess : 0m - excess) : null;

    /// <summary>
    /// What the proposal itself earned over the S&amp;P 500 had it been followed and executed, whatever the owner decided:
    /// name minus S&amp;P 500 for an Add or Hold, S&amp;P 500 minus name for a Trim or Exit. Null until scored.
    /// </summary>
    public static decimal? ProposalValuePct(Recommendation r)
        => ExcessReturnPct(r) is decimal excess ? (ProposesExposure(r.Action) ? excess : 0m - excess) : null;

    /// <summary>
    /// The side the owner's decision left them on: true when it kept or added the name, false when it reduced or declined
    /// it, null when there is no decision to sign (undecided, a confirmed order never placed, a rejected Hold).
    /// </summary>
    public static bool? KeptExposure(Recommendation r) => r.Status switch
    {
        RecommendationStatus.Confirmed when r.Execution == RecommendationExecution.Unexecuted => null,
        RecommendationStatus.Confirmed => ProposesExposure(r.Action),
        RecommendationStatus.Rejected when r.Action == RecommendationAction.Hold => null,
        RecommendationStatus.Rejected => !ProposesExposure(r.Action),
        _ => null,
    };

    /// <summary>An Add or Hold keeps or adds the name; a Trim or Exit reduces it.</summary>
    public static bool ProposesExposure(RecommendationAction action)
        => action is RecommendationAction.Add or RecommendationAction.Hold;
}
