namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Sustained relative-underperformance rule for the benchmark-relative track record (fs-699) — bound
/// from <c>appsettings.json</c> section <c>ThesisTrackRecord:RelativePerformance</c>. A scope is in
/// sustained underperformance when its <see cref="Window"/> excess return vs the benchmark sits at or
/// below −<see cref="ThresholdPct"/> percentage points on <see cref="SustainedRuns"/> consecutive
/// weekly runs. Book and sleeve scopes raise an alert on that; a single thesis only carries the flag,
/// because per-thesis relative underperformance is what a <c>relative_return</c> invalidation trigger
/// is for.
/// </summary>
public class RelativePerformanceConfig
{
    public const string SectionName = "ThesisTrackRecord:RelativePerformance";

    private const decimal DefaultThresholdPct = 5m;
    private const int DefaultSustainedRuns = 4;

    /// <summary>Underperformance margin in percentage points (5 = 5 pp behind the benchmark).</summary>
    public decimal ThresholdPct { get; set; } = DefaultThresholdPct;

    /// <summary>Consecutive weekly runs the margin must hold for (4 ≈ a month).</summary>
    public int SustainedRuns { get; set; } = DefaultSustainedRuns;

    /// <summary>The window whose excess return the rule reads.</summary>
    public TrackRecordWindow Window { get; set; } = TrackRecordWindow.ThreeMonths;
}
