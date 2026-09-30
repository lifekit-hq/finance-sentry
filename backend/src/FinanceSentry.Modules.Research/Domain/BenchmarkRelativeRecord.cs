namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// One materialized benchmark-relative track-record figure (fs-699): the return of a scope (book,
/// sleeve or thesis) against the benchmark over one window, computed by the weekly track-record job
/// from the persisted paired <see cref="ThesisEvent"/> series and stored so the API and the agent
/// read the figure rather than recomputing it. Rows are append-per-run: one run (<see cref="AsOf"/>)
/// writes one row per (scope, scope key, window), and a rerun on the same day replaces that run.
/// Returns are percentages (5.0 = 5%). Uncovered windows keep the row with null returns so the gap
/// is visible rather than silently missing.
/// </summary>
public class BenchmarkRelativeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    /// <summary>UTC day of the run that produced this row.</summary>
    public DateTimeOffset AsOf { get; set; }

    public TrackRecordScope Scope { get; set; }

    /// <summary>"book", the sleeve name, or the thesis id ("N" format).</summary>
    public string ScopeKey { get; set; } = string.Empty;

    /// <summary>Human label: "Book", the sleeve name, or the thesis ticker.</summary>
    public string Label { get; set; } = string.Empty;

    public Guid? ThesisId { get; set; }
    public TrackRecordWindow Window { get; set; }
    public string BenchmarkTicker { get; set; } = "SPY";

    /// <summary>True when the stored history spans the whole window.</summary>
    public bool Covered { get; set; }

    /// <summary>Theses whose series covered this window (1 or 0 for a thesis row).</summary>
    public int ConstituentCount { get; set; }

    public DateTimeOffset? FromTimestamp { get; set; }
    public DateTimeOffset? ToTimestamp { get; set; }
    public decimal? SubjectReturnPct { get; set; }
    public decimal? BenchmarkReturnPct { get; set; }
    public decimal? ExcessReturnPct { get; set; }

    /// <summary>Excess return net of trading cost and tax; null unless <see cref="NetGate"/> is Verified.</summary>
    public decimal? NetExcessReturnPct { get; set; }

    /// <summary>One of <see cref="NetExcessGate"/>.</summary>
    public string NetGate { get; set; } = NetExcessGate.NotHeld;

    /// <summary>Consecutive runs, this one included, whose excess sat at or below the underperformance threshold.</summary>
    public int UnderperformingRuns { get; set; }

    /// <summary><see cref="UnderperformingRuns"/> reached the configured sustained-run count.</summary>
    public bool SustainedUnderperformance { get; set; }

    public DateTimeOffset ComputedAt { get; set; } = DateTimeOffset.UtcNow;
}
