namespace FinanceSentry.Core.Connections;

/// <summary>
/// What one source's cadence or protocol makes its own in the connection-health policy (report §5.7). The alert
/// thresholds stay one shared set in <see cref="ConnectionHealthOptions"/>; only a value that differs by source
/// lives here. A source's values are named options, bound from <c>ConnectionHealth:Sources:&lt;source&gt;</c> over
/// the code defaults its module registers; a source with neither gets these defaults.
/// </summary>
public sealed class ConnectionHealthSourceOptions
{
    public const string SectionName = "Sources";

    /// <summary>
    /// How old the last success may get before a <see cref="FailureClass.NotReady"/> answer counts, like a transient
    /// failure from then on. Null: the source has no grace for it, so a NotReady answer counts at once rather than
    /// being hidden without a bound.
    /// </summary>
    public TimeSpan? NotReadyDeadline { get; set; }

    /// <summary>
    /// How long after a NotReady answer the source runs again instead of waiting for its next scheduled run.
    /// Null: it waits for that run.
    /// </summary>
    public TimeSpan? NotReadyRetryAfter { get; set; }
}
