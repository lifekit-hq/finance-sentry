namespace FinanceSentry.Modules.Research.Domain.Scoring;

using FinanceSentry.Modules.Research.Domain.Opportunity;

/// <summary>
/// The verdict vocabulary (spec 726 OQ-3). <see cref="NotScored"/> is the non-verdict: the scorecard
/// has nothing to read, or the name sits outside the v1 single-equity universe.
/// </summary>
public enum VerdictWord
{
    StrongBuy,
    Buy,
    Wait,
    Avoid,
    NotScored,
}

/// <summary>The scorecard axis a verdict condition reads.</summary>
public enum VerdictAxis
{
    Structure,
    Fundamentals,
    Crowding,
    Valuation,

    /// <summary>Revenue growth year over year (rules v2 growth gate on STRONG BUY).</summary>
    Growth,
}

/// <summary>
/// How a condition compares an axis value with its threshold. <see cref="IsMissing"/> makes a null
/// axis an explicit rule, never a mid value (spec 726 FR-008).
/// </summary>
public enum VerdictOperator
{
    LessThan,
    GreaterThan,
    AtLeast,
    AtMost,
    Is,
    IsNot,
    IsMissing,
}

/// <summary>
/// The axis facts the verdict reads. <see cref="StructureScore"/> is the regime-adjusted structure score
/// (spec 726 OQ-8). <see cref="Crowding"/> is null when the crowding axis has no extension data to
/// classify. <see cref="ValuationPremiumPct"/> is the trailing P/E's premium over its own five-year
/// average, in percent; null when either side is missing. From rules v2, when that history is missing the
/// forward P/E's premium over the peer median stands in for it: <see cref="ValuationPremiumPct"/> is then the
/// peer premium and <see cref="PriceLens"/> names which yardstick it is. <see cref="RevenueYoyPct"/> is revenue
/// growth year over year in percent, null when not evaluable.
/// </summary>
public sealed record VerdictInputs(
    int? StructureScore,
    int? FundamentalsScore,
    CrowdingClass? Crowding,
    bool ValuationNotApplicable,
    decimal? ValuationPremiumPct,
    decimal? RevenueYoyPct = null,
    string? PriceLens = null);

/// <summary>The yardsticks the price lens can read (<see cref="Verdict.PriceLens"/>).</summary>
public static class PriceLenses
{
    /// <summary>The trailing P/E against the ticker's own five-year average.</summary>
    public const string OwnHistory = "own_history";

    /// <summary>The forward P/E against the median of its peers (the fallback when the own history is missing).</summary>
    public const string Peers = "peers";
}

/// <summary>
/// Why a WAIT waits, in a code and a short label. Every rule that returns WAIT names one, so a WAIT is never a
/// bare word.
/// </summary>
public static class WaitReasons
{
    public const string StrongBusinessWeakTrend = "strong_business_weak_trend";
    public const string TrendWithoutBusiness = "trend_without_business";
    public const string NotEnoughData = "not_enough_data";
    public const string ShortOfBuyBar = "short_of_buy_bar";

    public static string? Label(string? reason) => reason switch
    {
        StrongBusinessWeakTrend => "Strong business, weak trend",
        TrendWithoutBusiness => "Trend without the business",
        NotEnoughData => "Not enough data",
        ShortOfBuyBar => "Short of the BUY bar",
        _ => null,
    };
}

/// <summary>
/// One threshold test inside a rule, with the value it read. Values are invariant-culture strings so a
/// reader can show and re-check them without the evaluator: <see cref="Actual"/> is null when the axis
/// is missing.
/// </summary>
public sealed record VerdictCondition(
    VerdictAxis Axis,
    VerdictOperator Operator,
    string? Threshold,
    string? Actual,
    bool Met);

/// <summary>One row of the ordered decision list, evaluated against this scorecard.</summary>
public sealed record VerdictRuleOutcome(
    string Id,
    VerdictWord Word,
    string Description,
    IReadOnlyList<VerdictCondition> Conditions,
    bool Matched,
    string? WaitReason = null);

/// <summary>
/// A verdict word with the rule table that produced it: the rules version, the first rule that matched
/// (the one that fired) and every rule's outcome in order (spec 726 FR-005/FR-006). Never a number
/// (FR-007). <see cref="WaitReason"/> is the named reason of a WAIT (a <see cref="WaitReasons"/> code), null for
/// every other word; <see cref="PriceLens"/> is the yardstick the valuation conditions read (a
/// <see cref="PriceLenses"/> code), null when the price lens had nothing to read.
/// </summary>
public sealed record Verdict(
    VerdictWord Word,
    int RulesVersion,
    string FiredRuleId,
    IReadOnlyList<VerdictRuleOutcome> Rules,
    string? WaitReason = null,
    string? PriceLens = null);
