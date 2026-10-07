namespace FinanceSentry.Modules.Research.Domain.Scoring;

using FinanceSentry.Modules.Research.Domain.Opportunity;

/// <summary>
/// In-memory result of scoring a candidate (US1). Sub-scores are 0-100 or null when not evaluable
/// (missing data, insufficient history, divide-by-zero) — never faked or defaulted (FR-002/FR-006).
/// No composite/aggregate field exists here or anywhere downstream (FR-007).
/// </summary>
/// <remarks>
/// <c>StructureScore</c> is always the <b>raw</b> value (governed by <c>FormulaVersion</c>). The
/// 021 <c>Regime</c> context, when present, carries the regime-adjusted structure score + rationale
/// alongside it — regime modulates the presented/ranked score without ever overwriting the raw one
/// (and never actions). Null when regime coupling produced nothing to attach.
/// </remarks>
public sealed record CandidateScorecard(
    int? StructureScore,
    int? FundamentalsScore,
    CrowdingClass Crowding,
    IpsFitFacts IpsFit,
    ScoreEvidence Evidence,
    int FormulaVersion,
    RegimeAdjustment? Regime = null);

/// <summary>Facts about how a candidate would sit against the user's IPS if promoted (FR-011a).</summary>
public sealed record IpsFitFacts(
    decimal? CurrentWeight,
    decimal? WouldBeWeight,
    decimal? MaxSinglePositionPct,
    bool WithinConcentration,
    string AssetClassFit,
    string? Note = null)
{
    public static IpsFitFacts Unknown { get; } = new(null, null, null, true, "unknown");
}

/// <summary>
/// Structured raw inputs behind every sub-score, for 100% explainability (SC-001/FR-002). The
/// <c>ValuationContext</c>/<c>BaseRateContext</c> slots are reserved for FR-006b/c (v1.1) — left
/// null in v1, never populated with placeholder data.
/// </summary>
public sealed record ScoreEvidence(
    IReadOnlyDictionary<int, decimal?> RsByWindow,
    IReadOnlyDictionary<int, decimal?> ReturnByWindow,
    decimal? ExtensionFromMa50,
    decimal? TodayZScore,
    decimal? VolumeRatio,
    IReadOnlyList<string> StructureNotEvaluableReasons,
    decimal? RevenueYoy,
    decimal? GrossMarginLatest,
    decimal? GrossMarginTrend,
    decimal? EpsYoy,
    IReadOnlyList<string> FundamentalsNotEvaluableReasons,
    string? ValuationContext = null,
    string? BaseRateContext = null,
    int? SectorRank = null,
    int? SectorRankDelta = null,
    decimal? DistanceFrom63dHigh = null,
    string? FundamentalsBasis = null)
{
    public static ScoreEvidence Empty { get; } = new(
        new Dictionary<int, decimal?>(),
        new Dictionary<int, decimal?>(),
        null, null, null, [], null, null, null, null, []);
}

/// <summary>A deterministically prefilled invalidation trigger proposed at promotion time (US3).</summary>
public sealed record ProposedTrigger(
    string Metric,
    string Direction,
    decimal Threshold,
    string? ProxyTicker,
    int ConsecutivePeriods,
    string Rationale);
