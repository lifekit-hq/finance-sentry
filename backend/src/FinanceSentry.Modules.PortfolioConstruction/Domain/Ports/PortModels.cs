namespace FinanceSentry.Modules.PortfolioConstruction.Domain.Ports;

/// <summary>
/// One symbol's scorecard as this module reads it: the four axes as facts and the verdict the rule table gave
/// them. There is no composite number anywhere on it (019 FR-007 as amended, spec 726 FR-007). Axis values are
/// null when the axis could not be evaluated, with the reasons beside them. <see cref="Sector"/> and
/// <see cref="Industry"/> are the company profile (null when the source gave none): the IPS sector exclusions
/// match on them (spec 726 FR-015), and they are never an input to a gate, an ordering or the verdict.
/// </summary>
public sealed record UniverseScorecard(
    string Symbol,
    string? FailureReason,
    DateTimeOffset? ScoredAt,
    int? StructureRaw,
    int? StructureAdjusted,
    string? StructureBasis,
    IReadOnlyList<string> StructureNotEvaluable,
    int? Fundamentals,
    IReadOnlyList<string> FundamentalsNotEvaluable,
    string? Crowding,
    decimal? ExtensionFromMa50,
    bool ValuationNotApplicable,
    decimal? TrailingPe,
    decimal? TrailingPeFiveYearAvg,
    decimal? ValuationPremiumPct,
    decimal? ImpliedUpsidePct,
    string? ValuationNotEvaluable,
    bool ValuationStale,
    string? VerdictWord,
    int? VerdictRulesVersion,
    string? FiredRuleId,
    string? FiredRuleDescription,
    string? Sector = null,
    string? Industry = null)
{
    public bool Scored => FailureReason is null;

    public static UniverseScorecard Failed(string symbol, string reason) =>
        new(symbol, reason, null, null, null, null, [], null, [], null, null, false, null, null, null, null, null, false,
            null, null, null, null);
}

/// <summary>
/// The viewer's own constraints. Percent values are whole percentage points of the whole book (0-100).
/// <see cref="Sleeves"/> is null when the viewer has no IPS, so the archetype's default sleeves apply
/// (spec 726 OQ-9). A null limit is unset.
/// </summary>
public sealed record ConstructionPolicy(
    IReadOnlyList<string> Exclusions,
    IReadOnlyList<PolicySleeve>? Sleeves,
    decimal? MaxPositionPct,
    decimal? MinCashPct)
{
    public static ConstructionPolicy None { get; } = new([], null, null, null);
}

/// <summary>One IPS asset-class sleeve target in whole percentage points.</summary>
public sealed record PolicySleeve(string AssetClass, decimal TargetPct);
