namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Modules.Research.Domain.Scoring;

/// <summary>
/// One symbol's scorecard as the four axes and the verdict word they map to, with every fact behind
/// each axis and the whole rule table (spec 726 US2, FR-005/FR-006). Computed at read time by the
/// unchanged 019 scorers; nothing here is stored. There is no composite, weighted or overall number
/// (019 FR-007, spec 726 FR-007, SC-003). Enums cross as their names. <see cref="Sector"/> and
/// <see cref="Industry"/> are the company profile Yahoo returns with the valuation metrics already read for
/// this scorecard (spec 726 FR-015): a fact about the name, not an axis, so neither feeds the verdict. Null
/// when the source gave none (or the name is not an equity); never guessed.
/// </summary>
public sealed record ScorecardResult(
    string Symbol,
    DateTimeOffset GeneratedAt,
    int FormulaVersion,
    ScorecardStructureAxis Structure,
    ScorecardFundamentalsAxis Fundamentals,
    ScorecardCrowdingAxis Crowding,
    ScorecardValuationAxis Valuation,
    VerdictDto Verdict,
    string? Sector = null,
    string? Industry = null);

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
    IReadOnlyList<string> NotEvaluableReasons,
    string? EpsYoyBasis = null);

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
/// reads it. Prices are in major units of <see cref="Currency"/>. When that premium is missing (no five-year
/// history) and the peers' forward P/Es are known, <see cref="PeerPremiumPct"/> is the forward P/E's distance
/// from the peer median in percent and the verdict reads it instead (rules v2); <see cref="PriceLens"/> names
/// the yardstick the verdict read (<see cref="PriceLenses"/>), null when neither exists.
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
    /// <summary>Median of the peers' positive forward P/Es; null unless the peer lens was read.</summary>
    public decimal? PeerForwardPeMedian { get; init; }

    /// <summary>Peers that had a positive forward P/E; null unless the peer lens was read.</summary>
    public int? PeerCount { get; init; }

    /// <summary>Forward P/E over the peer median, in percent; null unless the peer lens was read.</summary>
    public decimal? PeerPremiumPct { get; init; }

    /// <summary>The yardstick the verdict reads (<see cref="PriceLenses"/>), or null when it has none.</summary>
    public string? PriceLens { get; init; }

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
            false, trailingPe, fiveYearAvg, windowYears, premium, forwardPe, price, consensusTarget, upside, currency, isStale, reason)
        {
            PriceLens = premium is null ? null : PriceLenses.OwnHistory,
        };
    }

    /// <summary>The percentage the verdict's valuation conditions read: the own-history premium, else the peer premium.</summary>
    public decimal? LensPremium() => PremiumPct ?? PeerPremiumPct;

    /// <summary>
    /// Adds the forward P/E against the median of <paramref name="peerForwardPes"/> as facts. It needs a positive
    /// forward P/E and at least <paramref name="minPeers"/> peers with a positive forward P/E (a negative or absent
    /// one is not a price), and leaves the axis unchanged otherwise. The verdict reads it (<see cref="PriceLens"/>
    /// becomes <see cref="PriceLenses.Peers"/>) only when the own-history premium is missing; with that history the
    /// peer figures ride beside it as facts and the lens stays the own history.
    /// </summary>
    public ScorecardValuationAxis WithPeerLens(IEnumerable<decimal?> peerForwardPes, int minPeers)
    {
        if (NotApplicable || ForwardPe is not > 0m)
        {
            return this;
        }

        var peers = peerForwardPes.OfType<decimal>().Where(pe => pe > 0m).OrderBy(pe => pe).ToList();
        if (peers.Count < minPeers || peers.Count == 0)
        {
            return this;
        }

        var middle = peers.Count / 2;
        var median = peers.Count % 2 == 1 ? peers[middle] : (peers[middle - 1] + peers[middle]) / 2m;
        return this with
        {
            PeerForwardPeMedian = decimal.Round(median, Decimals),
            PeerCount = peers.Count,
            PeerPremiumPct = decimal.Round((ForwardPe.Value / median - 1m) * PercentScale, Decimals),
            PriceLens = PremiumPct is null ? PriceLenses.Peers : PriceLens,
        };
    }
}

/// <summary>
/// The verdict word with its rules version, the rule that fired, which structure value it read
/// (<see cref="StructureBasis"/>: <c>adjusted</c> or <c>raw</c> when there was no regime reading to
/// adjust by), and the whole ordered rule table as evaluated (spec 726 FR-006, FR-011).
/// <see cref="Reasons"/> and <see cref="TurnsBuyWhen"/> restate that table in words, derived from it
/// deterministically by <see cref="VerdictReasons"/> (no model text); <see cref="TurnsBuyWhen"/> is null
/// when the word is already BUY or better, or the name is not scored. They ride the verdict, so they
/// reach only the callers the verdict reaches (<c>ai.use</c>). <see cref="WaitReason"/> (a code) and
/// <see cref="WaitReasonLabel"/> (its short label) name why a WAIT waits and are null for every other word;
/// <see cref="PriceLens"/> says which yardstick the valuation conditions read (<c>own_history</c> or
/// <c>peers</c>), null when there was none.
/// </summary>
public sealed record VerdictDto(
    string Word,
    int RulesVersion,
    string FiredRuleId,
    string StructureBasis,
    IReadOnlyList<VerdictRuleDto> Rules,
    IReadOnlyList<VerdictReasonDto> Reasons,
    string? TurnsBuyWhen,
    string? WaitReason = null,
    string? WaitReasonLabel = null,
    string? PriceLens = null)
{
    public const string AdjustedBasis = "adjusted";
    public const string RawBasis = "raw";

    public static VerdictDto From(Verdict verdict, string structureBasis)
    {
        var explanation = VerdictReasons.Explain(verdict);
        return new(
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
                    c.Axis.ToString(), c.Operator.ToString(), c.Threshold, c.Actual, c.Met)).ToList())).ToList(),
            explanation.Reasons.Select(r => new VerdictReasonDto(r.Axis, r.Status.ToString(), r.Text, r.Decisive)).ToList(),
            explanation.TurnsBuyWhen,
            verdict.WaitReason,
            WaitReasons.Label(verdict.WaitReason),
            verdict.PriceLens);
    }
}

/// <summary>One fact behind the word: the axis, how it stands (<c>Pass</c>, <c>Fail</c>, <c>Missing</c>, <c>NotApplicable</c>) and the sentence with its value against the bar.</summary>
public sealed record VerdictReasonDto(string Axis, string Status, string Text, bool Decisive);

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

/// <summary>
/// One symbol's verdict word and the rule that fired, for a chip, plus the line saying what would turn it into BUY
/// (the same <see cref="VerdictDto.TurnsBuyWhen"/> the full scorecard carries; null when there is no bar to name).
/// Nothing else of the scorecard crosses. <see cref="WaitReasonLabel"/> is the named reason of a WAIT.
/// </summary>
public sealed record ScorecardVerdictSummary(
    string Symbol, string Word, string FiredRuleId, string? TurnsBuyWhen, string? WaitReasonLabel = null);
