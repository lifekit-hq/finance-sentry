namespace FinanceSentry.Modules.PortfolioConstruction.Domain;

/// <summary>Where an applied constraint came from (spec 726 FR-014).</summary>
public static class ConstraintSource
{
    public const string ArchetypeDefault = "archetype default";
    public const string YourRiskRules = "your risk rules";
    public const string YourIps = "your IPS";
}

/// <summary>One limit the construction applied, with the value and the source that set it (spec 726 FR-014).</summary>
public sealed record AppliedConstraint(string Key, string Label, decimal Value, string Source);

/// <summary>A sleeve of the proposed book. <see cref="BuiltFromUniverse"/> is false for sleeves with no named instruments in v1.</summary>
public sealed record ProposedSleeve(string AssetClass, decimal WeightPct, bool BuiltFromUniverse, string? Note);

/// <summary>One symbol's place in the construction: the weight it got, or why it got none.</summary>
public sealed record ConstructionEntry(
    string Symbol,
    int? Rank,
    decimal? WeightPct,
    string Reason);

/// <summary>
/// The result of one construction (spec 726 US3): the names with weight and reason, the sleeves, the cash, the
/// limits applied with their sources, and the notes explaining anything left out. <see cref="Refusal"/> is set when
/// the policy cannot be built as written (the IPS targets overshoot 100%), in which case no weight is given.
/// </summary>
public sealed record ConstructionOutcome(
    IReadOnlyDictionary<string, ConstructionEntry> Entries,
    IReadOnlyList<ProposedSleeve> Sleeves,
    decimal CashPct,
    IReadOnlyList<AppliedConstraint> Constraints,
    IReadOnlyList<string> Notes,
    string? Refusal);
