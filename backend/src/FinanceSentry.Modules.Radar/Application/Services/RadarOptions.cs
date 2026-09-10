namespace FinanceSentry.Modules.Radar.Application.Services;

using FinanceSentry.Modules.Radar.Domain;

/// <summary>
/// All Radar thresholds and toggles bound from configuration (section <c>Radar</c>). No magic
/// numbers in code (FR-008); the scanner launches in <see cref="ScannerMode.LogOnly"/> (FR-015).
/// </summary>
public sealed class RadarOptions
{
    public const string SectionName = "Radar";

    /// <summary>Log-only (default) records signals but raises zero Alerts until calibrated.</summary>
    public ScannerMode ScannerMode { get; set; } = ScannerMode.LogOnly;

    /// <summary>Trading-day lookback for the initial backfill (default 300).</summary>
    public int LookbackTradingDays { get; set; } = 300;

    /// <summary>Freshness bound N: latest bar older than this many trading days is stale (default 2).</summary>
    public int FreshnessMaxTradingDays { get; set; } = 2;

    /// <summary>Sector rank delta at/above this (absolute) emits a rotation_shift signal.</summary>
    public int RotationRankDeltaThreshold { get; set; } = 3;

    /// <summary>|z-score| at/above this emits an unusual_move signal.</summary>
    public decimal UnusualMoveZScore { get; set; } = 3m;

    /// <summary>|z-score| at/above this on a held ticker raises an Alert (Alerting mode only).</summary>
    public decimal UnusualMoveAlertZScore { get; set; } = 4m;

    /// <summary>Extension from the 50-day MA at/above this (fraction, e.g. 0.15 = 15%) emits an extended signal.</summary>
    public decimal ExtensionThreshold { get; set; } = 0.15m;

    /// <summary>Sector rank at/below (worst) this quartile bound flags a held ticker's sector as laggard.</summary>
    public int LaggardBottomQuartileRank { get; set; } = 9;

    /// <summary>Silence window (hours) for deduping notable+ signals by DedupKey.</summary>
    public int SilenceWindowHours { get; set; } = 24;

    /// <summary>Retention horizon (days) for info signals before pruning (default 2 years).</summary>
    public int InfoRetentionDays { get; set; } = 730;

    /// <summary>Hour of day (UTC) the ingestion job runs (post-US-close).</summary>
    public int IngestionHourUtc { get; set; } = 22;

    /// <summary>Hour of day (UTC) the compute job runs (shortly after ingestion).</summary>
    public int ComputeHourUtc { get; set; } = 23;

    /// <summary>Seed benchmark tickers.</summary>
    public string[] BenchmarkTickers { get; set; } = ["SPY"];

    /// <summary>Seed sector-ETF tickers (11 SPDR sectors).</summary>
    public string[] SectorTickers { get; set; } =
        ["XLB", "XLC", "XLE", "XLF", "XLI", "XLK", "XLP", "XLRE", "XLU", "XLV", "XLY"];

    /// <summary>Seed industry-ETF tickers.</summary>
    public string[] IndustryTickers { get; set; } = ["SMH"];

    /// <summary>The benchmark ticker RS is measured against.</summary>
    public string Benchmark { get; set; } = "SPY";

    /// <summary>
    /// Widens the universe with broad-market index constituents so momentum can be ranked beyond the
    /// book (#558). Off by default — same launch posture as <see cref="ScannerMode.LogOnly"/>.
    /// </summary>
    public bool BroadUniverseEnabled { get; set; }

    /// <summary>
    /// Upstream bar fetches an ingestion run may spend on index constituents. Holdings, watchlist and
    /// seed members are always ingested first; the budget rotates over the least-fresh constituents so
    /// successive runs converge on full coverage without one run fanning out over the whole index.
    /// Default 250 keeps a full rotation of the ~450-name seed inside
    /// <see cref="FreshnessMaxTradingDays"/> — a slower rotation would leave most constituents
    /// permanently stale, and stale structure is discarded downstream rather than ranked.
    /// </summary>
    public int BroadUniverseMaxIngestPerRun { get; set; } = 250;
}
