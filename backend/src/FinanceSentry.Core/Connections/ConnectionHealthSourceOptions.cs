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

    /// <summary>
    /// For a source that runs once a day: how long after a counted failure it looks again before the next scheduled
    /// run, one entry per look (1 h, then 4 h after that, for Flex), so a real outage is told within hours instead of
    /// on the third day. The list is the bound: when it is spent the chain stops. Empty: none; a source that runs
    /// every 15 or 30 minutes retries soon enough on its own schedule. Names intervals only; the source's job
    /// decides which failures earn one (never a rate-limited, not-ready or definitive credential failure).
    /// </summary>
    public TimeSpan[] EarlyRechecks { get; set; } = [];
}
