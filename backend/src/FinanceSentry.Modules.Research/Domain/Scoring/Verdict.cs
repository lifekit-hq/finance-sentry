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
}

/// <summary>
/// How a condition compares an axis value with its threshold. <see cref="IsMissing"/> and
/// <see cref="IsKnown"/> make a null axis an explicit rule, never a mid value (spec 726 FR-008).
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
    IsKnown,
}

/// <summary>
/// The axis facts the verdict reads. <see cref="StructureScore"/> is the regime-adjusted structure score
/// (spec 726 OQ-8). <see cref="Crowding"/> is null when the crowding axis has no extension data to
/// classify. <see cref="ValuationPremiumPct"/> is the trailing P/E's premium over its own five-year
/// average, in percent; null when either side is missing.
/// </summary>
public sealed record VerdictInputs(
    int? StructureScore,
    int? FundamentalsScore,
    CrowdingClass? Crowding,
    bool ValuationNotApplicable,
    decimal? ValuationPremiumPct);

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
    bool Matched);

/// <summary>
/// A verdict word with the rule table that produced it: the rules version, the first rule that matched
/// (the one that fired) and every rule's outcome in order (spec 726 FR-005/FR-006). Never a number
/// (FR-007).
/// </summary>
public sealed record Verdict(
    VerdictWord Word,
    int RulesVersion,
    string FiredRuleId,
    IReadOnlyList<VerdictRuleOutcome> Rules);
