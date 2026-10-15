namespace FinanceSentry.Core.Connections;

/// <summary>
/// Thresholds of the connection-health policy, bound from the <c>ConnectionHealth</c> config section.
/// The defaults are the captain's decision of 2026-10-06 (report §13): the user hears of a non-credential
/// failure after at least 3 failures and 2 h without success, and a connection failing for 7 days goes Dormant.
/// </summary>
public sealed class ConnectionHealthOptions
{
    public const string SectionName = "ConnectionHealth";

    /// <summary>Consecutive failures before a connection can become Failing.</summary>
    public int FailingMinConsecutiveFailures { get; set; } = 3;

    /// <summary>Time without a success before a connection can become Failing.</summary>
    public TimeSpan FailingMinTimeWithoutSuccess { get; set; } = TimeSpan.FromHours(2);

    /// <summary>Continuous failure after which a connection becomes Dormant.</summary>
    public TimeSpan DormantAfter { get; set; } = TimeSpan.FromDays(7);

    /// <summary>A suspect credential failure is confirmed when seen again at least this long after the first.</summary>
    public TimeSpan SuspectConfirmationWindow { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// While a connection is ActionRequired its scheduled runs pause; one run in this interval, timed from the
    /// last failure, probes it so a loss fixed on the provider's side recovers without a reconnect.
    /// </summary>
    public TimeSpan ActionRequiredProbeInterval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// The slowed cadence of a Dormant connection of an active provider: its scheduled runs pause, and one run in this
    /// interval, timed from the last failure, still tries it, so a recovery is picked up within a day
    /// (<see cref="ConnectionHealthPolicy.DormantPausedUntil"/>).
    /// </summary>
    public TimeSpan DormantSyncInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long after the failure that raised a suspect credential signal an active provider looks again once, so a
    /// blip clears within minutes; the look after <see cref="SuspectConfirmationWindow"/> still decides a loss
    /// (<see cref="ConnectionHealthPolicy.SuspectRecheckAfter"/>). The captain's "try again after a minute". Zero turns the
    /// re-checks off.
    /// </summary>
    public TimeSpan SuspectQuickRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The providers whose alerts, Dormant cadence and suspect re-checks the policy drives, by the keys the shadow log uses
    /// (<c>monobank</c>, <c>truelayer</c>, <c>ibkr</c>, <c>ibkr-flex</c>, <c>binance</c>, <c>revolut_x</c>, …). Every other
    /// provider stays in shadow mode: its evaluation is recorded and logged, and its own alerting, account status and sync
    /// schedule stay exactly as they were (the pause on ActionRequired and on <see cref="ConnectionHealth.NotBefore"/>
    /// applies to every provider). Empty by default, so every provider starts in shadow.
    /// </summary>
    public List<string> ActiveProviders { get; set; } = [];

    /// <summary>Whether the policy drives <paramref name="provider"/>'s alerts (see <see cref="ActiveProviders"/>).</summary>
    public bool IsActive(string provider) => ActiveProviders.Contains(provider, StringComparer.OrdinalIgnoreCase);
}
