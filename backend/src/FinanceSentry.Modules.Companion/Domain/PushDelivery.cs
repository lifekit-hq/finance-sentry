namespace FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// The push ledger: one row per (event, subscription), unique, so overlapping sender runs never deliver twice
/// (spec 859 FR-007). Kept apart from <see cref="CompanionEvent.Disposition"/>, which push never touches (FR-001).
/// </summary>
public sealed class PushDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The companion event this delivery pushes; null for an alert pushed directly (<see cref="AlertId"/>).</summary>
    public Guid? EventId { get; set; }

    /// <summary>The alert this delivery pushes without a companion event (the deduped rating-change alert); null for an event delivery.</summary>
    public Guid? AlertId { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid UserId { get; set; }

    public PushDeliveryStatus Status { get; set; } = PushDeliveryStatus.Pending;

    public int Attempts { get; set; }

    public int? LastStatusCode { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
