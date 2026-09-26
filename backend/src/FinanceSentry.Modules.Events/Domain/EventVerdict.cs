namespace FinanceSentry.Modules.Events.Domain;

/// <summary>
/// The reader's recorded judgement on one fired event, keyed by the companion event id the reader
/// holds (wake payload / pending-events pull). One row per (user, event) - the unique index on
/// (UserId, CompanionEventId) is what makes "exactly one verdict per handled event" a fact of the
/// schema, not a promise the caller keeps; a later record replaces it. <see cref="AlertId"/> is the
/// alert the companion row was captured from, persisted at record time so the feed can still find the
/// verdict after the companion row purges (90 days) while the alert row lives on - it is null for an
/// event not captured from an alert (e.g. <c>AnalystAction</c>), which can and must still carry a
/// verdict. <see cref="Notified"/> says whether the reader told the user or judged the event
/// immaterial - both are verdicts; an acknowledged event with no row at all is silence.
/// </summary>
public sealed class EventVerdict
{
    public const int MaxVerdictLength = 2000;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid CompanionEventId { get; set; }
    public Guid? AlertId { get; set; }
    public string Verdict { get; set; } = string.Empty;
    public bool Notified { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}
