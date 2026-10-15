namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Modules.Research.Domain.Scoring;

/// <summary>
/// One symbol's scorecard as the four axes and the verdict word they map to, with every fact behind
/// each axis and the whole rule table (spec 726 US2, FR-005/FR-006). Computed at read time by the
/// unchanged 019 scorers; nothing here is stored. There is no composite, weighted or overall number
/// (019 FR-007, spec 726 FR-007, SC-003). Enums cross as their names.
/// </summary>
public sealed record ScorecardResult(
    string Symbol,
    DateTimeOffset GeneratedAt,
    int FormulaVersion,
    ScorecardStructureAxis Structure,
    ScorecardFundamentalsAxis Fundamentals,
    ScorecardCrowdingAxis Crowding,
    ScorecardValuationAxis Valuation,
    VerdictDto Verdict);

/// <summary>
/// Structure: the raw 0-100 score and, beside it, the regime-adjusted one the verdict reads (spec 726
/// OQ-8, FR-011). <see cref="RegimeRationale"/> says why they differ (<c>no_regime_data</c> when no
/// regime reading exists, in which case they are equal). Fractions stay fractions (0.12 = 12%).
/// </summary>
public sealed record ScorecardStructureAxis(
    int? RawScore,
    int? AdjustedScore,
    int AdjustmentPoints,
    IReadOnlyList<string> RegimeRationale,
    string? VolatilityRegime,
    string? RatesRegime,
    DateTimeOffset? RegimeAsOf,
    IReadOnlyDictionary<int, decimal?> RsByWindow,
    IReadOnlyDictionary<int, decimal?> ReturnByWindow,
    decimal? ExtensionFromMa50,
    decimal? TodayZScore,
    decimal? VolumeRatio,
    int? SectorRank,
    int? SectorRankDelta,
    decimal? DistanceFrom63dHigh,
    IReadOnlyList<string> NotEvaluableReasons);

/// <summary>Fundamentals: the 0-100 score or null with the reasons, and the facts it read.</summary>
public sealed record ScorecardFundamentalsAxis(
    int? Score,
    decimal? RevenueYoy,
    decimal? GrossMarginLatest,
    decimal? GrossMarginTrend,
    decimal? EpsYoy,
    string? Basis,
    IReadOnlyList<string> NotEvaluableReasons);

/// <summary>
/// Crowding: <c>Early</c>, <c>Normal</c> or <c>Extended</c>, with the thresholds that classify it.
/// <see cref="Class"/> is null when there is no extension-from-MA50 to classify (spec 726 FR-008: an
/// unknown axis is not read as Normal).
/// </summary>
public sealed record ScorecardCrowdingAxis(
    string? Class,
    decimal? ExtensionFromMa50,
    decimal? VolumeRatio,
    decimal EarlyExtensionThreshold,
    decimal ExtendedExtensionThreshold,
    decimal ExtendedVolumeRatioThreshold,
    string? NotEvaluableReason);

/// <summary>
/// Valuation as raw facts, never a 0-100 score (spec 726 FR-004). <see cref="PremiumPct"/> is the
/// trailing P/E's distance from its own five-year average in percent (31 vs 24 is 29.2); the verdict
/// reads it. Prices are in major units of <see cref="Currency"/>.
/// </summary>
public sealed record ScorecardValuationAxis(
    bool NotApplicable,
    decimal? TrailingPe,
    decimal? TrailingPeFiveYearAvg,
    int? HistoryWindowYears,
    decimal? PremiumPct,
    decimal? ForwardPe,
    decimal? Price,
    decimal? ConsensusTarget,
    decimal? ImpliedUpsidePct,
    string? Currency,
    bool IsStale,
    string? NotEvaluableReason)
{
    public const string ReasonNotApplicable = "not_applicable";
    public const string ReasonUnavailable = "valuation_unavailable";
    public const string ReasonNoTrailingPe = "no_trailing_pe";
    public const string ReasonNoFiveYearAverage = "no_five_year_average";

    private const int PercentScale = 100;
    private const int Decimals = 1;

    public static ScorecardValuationAxis Unavailable { get; } =
        new(false, null, null, null, null, null, null, null, null, null, false, ReasonUnavailable);

    public static ScorecardValuationAxis NotApplicableAxis { get; } =
        new(true, null, null, null, null, null, null, null, null, null, false, ReasonNotApplicable);

    /// <summary>The axis from a valuation snapshot already read (prices in major units).</summary>
    public static ScorecardValuationAxis From(ValuationSnapshotResult? snapshot) =>
        snapshot switch
        {
            null => Unavailable,
            { NotApplicable: true } => NotApplicableAxis,
            _ => From(
                snapshot.Metrics.TrailingPe.Value,
                snapshot.Metrics.TrailingPe.FiveYearAvg,
                snapshot.Metrics.TrailingPe.HistoryWindowYears,
                snapshot.Metrics.ForwardPe.Value,
                snapshot.Price,
                snapshot.ConsensusTarget,
                snapshot.Currency,
                snapshot.IsStale),
        };

    public static ScorecardValuationAxis From(
        decimal? trailingPe,
        decimal? fiveYearAvg,
        int? windowYears,
        decimal? forwardPe,
        decimal? price,
        decimal? consensusTarget,
        string? currency,
        bool isStale)
    {
        var premium = trailingPe is > 0m && fiveYearAvg is > 0m
            ? decimal.Round((trailingPe.Value / fiveYearAvg.Value - 1m) * PercentScale, Decimals)
            : (decimal?)null;
        var upside = consensusTarget is { } target && price is > 0m
            ? decimal.Round((target / price.Value - 1m) * PercentScale, Decimals)
            : (decimal?)null;
        var reason = premium is not null ? null
            : trailingPe is not > 0m ? ReasonNoTrailingPe
            : ReasonNoFiveYearAverage;

        return new ScorecardValuationAxis(
            false, trailingPe, fiveYearAvg, windowYears, premium, forwardPe, price, consensusTarget, upside, currency, isStale, reason);
    }
}

/// <summary>
/// The verdict word with its rules version, the rule that fired, which structure value it read
/// (<see cref="StructureBasis"/>: <c>adjusted</c> or <c>raw</c> when there was no regime reading to
/// adjust by), and the whole ordered rule table as evaluated (spec 726 FR-006, FR-011).
/// </summary>
public sealed record VerdictDto(
    string Word,
    int RulesVersion,
    string FiredRuleId,
    string StructureBasis,
    IReadOnlyList<VerdictRuleDto> Rules)
{
    public const string AdjustedBasis = "adjusted";
    public const string RawBasis = "raw";

    public static VerdictDto From(Verdict verdict, string structureBasis) =>
        new(
            verdict.Word.ToString(),
            verdict.RulesVersion,
            verdict.FiredRuleId,
            structureBasis,
            verdict.Rules.Select(r => new VerdictRuleDto(
                r.Id,
                r.Word.ToString(),
                r.Description,
                r.Matched,
                r.Id == verdict.FiredRuleId,
                r.Conditions.Select(c => new VerdictConditionDto(
                    c.Axis.ToString(), c.Operator.ToString(), c.Threshold, c.Actual, c.Met)).ToList())).ToList());
}

/// <summary>One rule of the table: matched when every condition is met; fired when it is the first match.</summary>
public sealed record VerdictRuleDto(
    string Id,
    string Word,
    string Description,
    bool Matched,
    bool Fired,
    IReadOnlyList<VerdictConditionDto> Conditions);

/// <summary>One condition with the threshold and the value it read (null = the axis is missing).</summary>
public sealed record VerdictConditionDto(
    string Axis,
    string Operator,
    string? Threshold,
    string? Actual,
    bool Met);
