namespace FinanceSentry.Modules.Retention.Application;

/// <summary>
/// Tunables for the retention engine (feature 024), bound from the <c>Retention:</c> config section.
/// Registry windows are the defaults; <see cref="WindowOverrides"/> lets the operator retune a table
/// without a code change.
/// </summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>UTC hour the nightly purge runs.</summary>
    public int PurgeHourUtc { get; set; } = 3;

    /// <summary>UTC hour the (opt-in) downsample job runs.</summary>
    public int DownsampleHourUtc { get; set; } = 4;

    /// <summary>Default delete batch size when a policy does not specify one.</summary>
    public int DefaultBatchSize { get; set; } = 5000;

    /// <summary>
    /// Per-table window overrides keyed by qualified table name (e.g. <c>bank_sync.SyncJobs</c> → 30).
    /// Absent keys fall back to the registry default.
    /// </summary>
    public Dictionary<string, int> WindowOverrides { get; set; } = [];

    /// <summary>US3 downsampling is opt-in; off by default.</summary>
    public DownsampleOptions Downsample { get; set; } = new();

    public sealed class DownsampleOptions
    {
        public bool Enabled { get; set; }
    }
}
