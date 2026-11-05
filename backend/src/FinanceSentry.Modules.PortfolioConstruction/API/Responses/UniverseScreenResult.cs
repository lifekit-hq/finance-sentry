namespace FinanceSentry.Modules.PortfolioConstruction.API.Responses;

/// <summary>
/// The ranked universe screen for one archetype (spec 726 US1, US3): every name in the scored universe with its
/// four axes, its verdict word and the rule that fired, and, where the archetype builds it, a suggested weight
/// with the reason that set it. Nothing here is a composite, weighted or overall score (019 FR-007 as amended,
/// spec 726 FR-007, SC-003): <see cref="UniverseRowDto.ConstructionRank"/> is the position in the archetype's
/// lexicographic ordering of the names it builds, not a number computed from the axes. Informational, not advice.
/// </summary>
public sealed record UniverseScreenResult(
    string Archetype,
    IReadOnlyList<ArchetypeOptionDto> Archetypes,
    DateTimeOffset GeneratedAt,
    int UniverseCap,
    bool Truncated,
    ConstructionDto Construction,
    IReadOnlyList<UniverseRowDto> Rows);

/// <summary>An archetype the viewer can pick, with the gates it builds from.</summary>
public sealed record ArchetypeOptionDto(
    string Key,
    string Label,
    string Description,
    IReadOnlyList<string> EligibleVerdicts,
    int? MinStructure,
    int? MinFundamentals,
    bool ExcludeExtended,
    IReadOnlyList<string> Ordering);

/// <summary>
/// The limits applied, the sleeves and cash of the suggested book, and the notes that explain anything left out.
/// <see cref="Refusal"/> is set when the policy cannot be built as written, and no weight is then given.
/// </summary>
public sealed record ConstructionDto(
    IReadOnlyList<AppliedConstraintDto> Constraints,
    IReadOnlyList<SleeveDto> Sleeves,
    decimal CashPct,
    IReadOnlyList<string> Notes,
    string? Refusal,
    int NamesHeld);

public sealed record AppliedConstraintDto(string Key, string Label, decimal Value, string Source);

public sealed record SleeveDto(string AssetClass, decimal WeightPct, bool BuiltFromUniverse, string? Note);

/// <summary>One symbol: the four axes as facts, the verdict with its rule, and its place in the suggested book.</summary>
public sealed record UniverseRowDto(
    string Symbol,
    IReadOnlyList<string> Sources,
    string? FailureReason,
    DateTimeOffset? ScoredAt,
    RowVerdictDto? Verdict,
    RowStructureDto? Structure,
    RowFundamentalsDto? Fundamentals,
    RowCrowdingDto? Crowding,
    RowValuationDto? Valuation,
    int? ConstructionRank,
    decimal? SuggestedWeightPct,
    string Reason);

/// <summary>The verdict word (enum name), the rules version and the rule that fired, in words (spec 726 FR-006).</summary>
public sealed record RowVerdictDto(string Word, int? RulesVersion, string? FiredRuleId, string? FiredRule);

/// <summary>Raw structure beside the adjusted value the verdict reads, and which of the two it read (spec 726 FR-011).</summary>
public sealed record RowStructureDto(int? Raw, int? Adjusted, string? Basis, IReadOnlyList<string> NotEvaluable);

public sealed record RowFundamentalsDto(int? Score, IReadOnlyList<string> NotEvaluable);

public sealed record RowCrowdingDto(string? Class, decimal? ExtensionFromMa50);

/// <summary>Valuation as raw facts, never a score (spec 726 FR-004).</summary>
public sealed record RowValuationDto(
    bool NotApplicable,
    decimal? TrailingPe,
    decimal? TrailingPeFiveYearAvg,
    decimal? PremiumPct,
    decimal? ImpliedUpsidePct,
    string? NotEvaluable,
    bool Stale);
