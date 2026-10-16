namespace FinanceSentry.Modules.Alerts.Domain;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;

public class Alert : IHasUpdatedAt, IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Type { get; set; } = AlertType.LowBalance;
    public string Severity { get; set; } = AlertSeverity.Warning;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    public string? ReferenceLabel { get; set; }
    /// <summary>
    /// Where in the app this alert opens (<c>AlertAppPath</c>), written by the emitter that knows what it is about. Null on
    /// alerts stored before it existed; <see cref="AlertAppPaths.For"/> resolves those from type, reference and label.
    /// </summary>
    public string? AppPath { get; set; }
    /// <summary>
    /// The connection-health state that raised the alert (<c>Failing</c>, <c>ActionRequired</c>, <c>Dormant</c>), for a
    /// sync-failure or Dormant alert the connection-health policy raised for a provider it drives; null on every other
    /// alert. Companion reads it to tell a reconnect ask from an outage it may hold for the digest.
    /// </summary>
    public string? HealthState { get; set; }
    public bool IsRead { get; set; }
    public bool IsResolved { get; set; }
    public bool IsDismissed { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
    /// <summary>"Accept" or "Defer" — null until the user taps the one-tap acknowledgement (432 US3).</summary>
    public string? AcknowledgementDecision { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    /// <summary>How many times a suppressed repeat has bumped this row instead of inserting a new one (finance-sentry#419 S5).</summary>
    public int OccurrenceCount { get; set; } = 1;
    public DateTimeOffset LastOccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
