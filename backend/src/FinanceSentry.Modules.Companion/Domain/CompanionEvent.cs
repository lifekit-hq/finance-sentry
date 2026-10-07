namespace FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// One captured material event and its lifecycle — the outbound outbox row (feature 031). Deduped by
/// <see cref="DedupKey"/> (unique). Recorded regardless of mode so nothing is lost (FR-007).
/// </summary>
public sealed class CompanionEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public CompanionEventKind Kind { get; set; }

    /// <summary>Ticker or rule key the event is about.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Mirrors the source severity (info/warning/critical).</summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>Short human line for the agent to triage from.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Logical identity — collapses re-detection of the same event.</summary>
    public string DedupKey { get; set; } = string.Empty;

    /// <summary>Source row (alert id / thesis id / analyst action id).</summary>
    public Guid? ReferenceId { get; set; }

    /// <summary>
    /// Where in the app the event opens (a relative path on an existing route, e.g. <c>/assets/NVDA</c>), resolved by the
    /// source module. Null when nothing in the app shows what it is about, and on events captured before it existed.
    /// </summary>
    public string? AppPath { get; set; }

    /// <summary>Originating module (<c>alerts</c> / <c>research</c>).</summary>
    public string SourceModule { get; set; } = string.Empty;

    public EventDisposition Disposition { get; set; } = EventDisposition.Pending;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? DispatchedAt { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}
