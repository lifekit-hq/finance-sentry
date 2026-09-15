namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain.Opportunity;

/// <summary>
/// All Opportunity Scanner thresholds bound from configuration (section <c>Opportunity</c>).
/// No magic numbers in the scorers or lifecycle handlers (FR-008 parity with Radar).
/// </summary>
public sealed class OpportunityOptions
{
    public const string SectionName = "Opportunity";

    /// <summary>Extension-from-MA50 (fraction) at/above this classifies crowding as Extended.</summary>
    public decimal ExtendedExtensionThreshold { get; set; } = 0.20m;

    /// <summary>Extension-from-MA50 (fraction) at/below this classifies crowding as Early.</summary>
    public decimal EarlyExtensionThreshold { get; set; } = 0.05m;

    /// <summary>Volume ratio at/above this, combined with Extended-range extension, confirms Extended crowding.</summary>
    public decimal ExtendedVolumeRatioThreshold { get; set; } = 1.5m;

    /// <summary>Structure/Fundamentals score at/above this bar triggers a top-tier ("notable") signal + Alert.</summary>
    public int TopTierScoreBar { get; set; } = 80;

    /// <summary>
    /// Whether a machine-scan nomination that clears <see cref="TopTierScoreBar"/> raises an Alert or
    /// only records its signal. Log-only (default) is the launch posture the #558 funnel needs: a scan
    /// reaching past the book can clear the bar on several unfamiliar names a night, once per user.
    /// User and Ledger nominations are deliberate acts and alert regardless of this mode.
    /// </summary>
    public ScanAlertMode ScanAlertMode { get; set; } = ScanAlertMode.LogOnly;

    /// <summary>Days after creation an Active candidate auto-expires if never promoted/rejected.</summary>
    public int CandidateTtlDays { get; set; } = 30;

    /// <summary>Default drawdown fraction used to prefill the price_drawdown invalidation trigger.</summary>
    public decimal DefaultDrawdownPrefill { get; set; } = 0.30m;

    /// <summary>Consecutive trading days the default drawdown trigger requires.</summary>
    public int DefaultDrawdownConsecutiveDays { get; set; } = 3;

    /// <summary>Buffer subtracted from the latest gross margin when prefilling a gross_margin trigger.</summary>
    public decimal GrossMarginPrefillBuffer { get; set; } = 0.10m;

    /// <summary>Bumped whenever the scoring normalization rules change, so old scorecards stay honest (FR-002).</summary>
    public int FormulaVersion { get; set; } = 1;

    /// <summary>Sector rotation ranks 1..N qualify as "top rotating" for scan rule (a) (FR-008a).</summary>
    public int ScanTopRotatingSectors { get; set; } = 2;

    /// <summary>Universe RS percentile at/above this is "top-quartile" for scan rule (a).</summary>
    public int ScanTopQuartileRsPercentile { get; set; } = 75;

    /// <summary>
    /// Universe RS percentile at/above this is "top-decile" for scan rule (b) (FR-008b). Held at 90
    /// through the #558 funnel: the rank is inclusive, so the rule's absolute yield already tracks the
    /// universe — it named ~50 of a 500-wide ingested index and names ~6 of a shortlist-sized one.
    /// Loosening it would re-widen discovery on the very axis stage 1 just screened.
    /// </summary>
    public int ScanTopDecileRsPercentile { get; set; } = 90;

    /// <summary>Volume ratio at/above this counts as above-average for the breakout rule (c) (FR-008c).</summary>
    public decimal ScanBreakoutVolumeRatioMin { get; set; } = 1.2m;

    /// <summary>
    /// Most nominations a single scan run may score; excess is logged and dropped. Calibrated for the
    /// #558 funnel: over a shortlist-sized universe the three rules nominate ~10-15 names that stage 1
    /// already screened out of the whole index, so a cap of 5 discarded most of a pre-screened slate.
    /// At 8 the cut trims the tail rather than the body, and the alert fan-out the old cap doubled as a
    /// guard against is now held by <see cref="ScanAlertMode"/>.
    /// </summary>
    public int ScanMaxNominationsPerRun { get; set; } = 8;

    /// <summary>
    /// Momentum-ranked nominations whose EDGAR fundamentals a scan run grades before the combined
    /// quality x momentum re-rank. Bounds the upstream EDGAR fan-out once the broad universe is on.
    /// </summary>
    public int ScanQualityShortlistSize { get; set; } = 25;

    /// <summary>Weight (0-1) of the fundamentals grade in the combined scan score; the rest is the RS percentile.</summary>
    public decimal ScanQualityWeight { get; set; } = 0.5m;

    /// <summary>Fundamentals grade at/above this earns the stable quality x momentum nomination reason.</summary>
    public int ScanQualityLeaderScore { get; set; } = 60;

    // ── #558 stage 1: the mechanical pre-filter that bounds what stage 2 pays bar math for ──
    /// <summary>
    /// Tickers the stage-1 pre-filter hands to stage 2 — the cap on how far past the book the scan's
    /// per-ticker bar math reaches. Tens, never the whole index; the funnel is pointless above that.
    /// </summary>
    public int ScanShortlistSize { get; set; } = 40;

    /// <summary>
    /// Constituents whose EDGAR fundamentals a stage-1 run grades. Bounds the upstream fan-out: the
    /// index is ranked on quote + street signals first and only this many names are graded.
    /// </summary>
    public int ScanShortlistGradeBudget { get; set; } = 60;

    /// <summary>Weight (0-1) of the street-action signal in the stage-1 surface score; the rest is coarse momentum.</summary>
    public decimal ScanShortlistStreetWeight { get; set; } = 0.3m;

    /// <summary>Surface-score points (capped at 100) each recent favourable street action is worth.</summary>
    public int ScanShortlistStreetActionPoints { get; set; } = 50;

    /// <summary>Weight (0-1) of the fundamentals grade in the stage-1 shortlist score; the rest is the surface score.</summary>
    public decimal ScanShortlistQualityWeight { get; set; } = 0.5m;

    /// <summary>Days back a stage-1 run counts upgrades, new coverage and target raises over.</summary>
    public int ScanShortlistActionLookbackDays { get; set; } = 14;

    /// <summary>
    /// Most street actions a stage-1 run reads from the feed before counting them per ticker. The
    /// action repository clamps any read to 200, so a larger value here buys nothing — raise the
    /// repository's own ceiling first if the window ever needs more.
    /// </summary>
    public int ScanShortlistActionLimit { get; set; } = 200;

    /// <summary>Hour (UTC) the daily opportunity scan runs — after Radar's 23:00 UTC compute job has refreshed structure.</summary>
    public int ScanHourUtc { get; set; } = 0;

    // ── 021 regime context (structure-score haircuts; context only, never actions) ──
    /// <summary>Structure-score haircut for an Extended-crowding candidate in a Panic volatility regime.</summary>
    public int RegimePanicExtendedHaircut { get; set; } = 15;

    /// <summary>Structure-score haircut for an Extended-crowding candidate in a Stressed volatility regime.</summary>
    public int RegimeStressedExtendedHaircut { get; set; } = 8;

    /// <summary>Additional structure-score haircut for an Extended-crowding candidate when the curve is Inverted.</summary>
    public int RegimeInvertedExtendedHaircut { get; set; } = 5;
}
