namespace FinanceSentry.Modules.Events.Domain.Ports;

/// <summary>
/// The companion outbox's view of a fired alert: whether and how it reached the reader (adapter over
/// the Companion module). <c>Disposition</c> is the Companion <c>EventDisposition</c> name as a string.
/// </summary>
public interface IEventDeliveryReader
{
    /// <summary>Outbox rows captured from the given alerts; an alert with no row yet is simply absent.</summary>
    Task<IReadOnlyList<EventDeliveryRecord>> ListForAlertsAsync(
        Guid userId, IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default);

    /// <summary>The outbox row with this id, only when it belongs to the user.</summary>
    Task<EventDeliveryRecord?> FindAsync(Guid userId, Guid eventId, CancellationToken ct = default);
}

public sealed record EventDeliveryRecord(
    Guid EventId,
    Guid? AlertId,
    string Kind,
    string Disposition,
    DateTimeOffset OccurredAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeliveredAt);
