namespace FinanceSentry.Modules.Events.Domain;

/// <summary>
/// What the reader made of a fired event, derived from the companion outbox disposition and the
/// presence of a recorded verdict (feature 049, US3). Silence is a first-class outcome: an event the
/// reader acknowledged (<c>Delivered</c>) without recording anything is <see cref="Silent"/>, never
/// dressed up as a verdict. Disposition strings are the Companion module's <c>EventDisposition</c>
/// names, carried as strings so this module never references Companion.
/// </summary>
public static class EventOutcome
{
    public const string Verdict = "verdict";
    public const string JudgedImmaterial = "judged_immaterial";
    public const string Silent = "silent";
    public const string Awaiting = "awaiting";
    public const string NotDelivered = "not_delivered";

    private static readonly IReadOnlySet<string> NotDeliveredDispositions = new HashSet<string>(StringComparer.Ordinal)
    {
        "SuppressedByMode", "SuppressedByRateLimit", "SuppressedByDedup", "Failed",
    };

    private const string DeliveredDisposition = "Delivered";

    public static string From(string? disposition, EventVerdict? verdict)
    {
        if (verdict is not null)
        {
            return verdict.Notified ? Verdict : JudgedImmaterial;
        }

        if (disposition is null)
        {
            return Awaiting;
        }

        if (disposition == DeliveredDisposition)
        {
            return Silent;
        }

        return NotDeliveredDispositions.Contains(disposition) ? NotDelivered : Awaiting;
    }
}
