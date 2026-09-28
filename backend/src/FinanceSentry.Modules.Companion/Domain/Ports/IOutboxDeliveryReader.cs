namespace FinanceSentry.Modules.Companion.Domain.Ports;

/// <summary>
/// Published read port (#673): the companion outbox's delivery view of a user's events, for
/// cross-module readers such as the Events module. Implemented inside Companion; the
/// FinanceSentry.Integration adapter reaches Companion only through this interface.
/// </summary>
public interface IOutboxDeliveryReader
{
    /// <summary>Outbox rows captured from the given alerts; an alert with no row yet is simply absent.</summary>
    Task<IReadOnlyList<OutboxDeliveryRecord>> ListForAlertsAsync(
        Guid userId, IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default);

    /// <summary>The outbox row with this id, only when it belongs to the user.</summary>
    Task<OutboxDeliveryRecord?> FindAsync(Guid userId, Guid eventId, CancellationToken ct = default);

    /// <summary>Every outbox row for the user that occurred on the given UTC day, any kind or disposition.</summary>
    Task<IReadOnlyList<OutboxDeliveryRecord>> ListForDateAsync(
        Guid userId, DateOnly date, CancellationToken ct = default);
}

/// <summary>
/// One outbox row as a cross-module reader sees it. <c>Kind</c> and <c>Disposition</c> are the
/// Companion enum names as strings; <c>AlertId</c> is read back from the row's dedup key and is
/// null for a row not captured from an alert.
/// </summary>
public sealed record OutboxDeliveryRecord(
    Guid EventId,
    Guid? AlertId,
    string Kind,
    string Subject,
    string Disposition,
    DateTimeOffset OccurredAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeliveredAt);
