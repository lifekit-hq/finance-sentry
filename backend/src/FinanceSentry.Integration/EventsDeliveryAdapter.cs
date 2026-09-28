namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Companion.Domain.Ports;
using FinanceSentry.Modules.Events.Domain.Ports;

/// <summary>
/// Feature 049 - <see cref="IEventDeliveryReader"/> over the Companion module's published
/// <see cref="IOutboxDeliveryReader"/> port (#673). Companion owns the dedup-key lookup and reads
/// the alert id back from the same key; kinds and dispositions cross as their enum names.
/// </summary>
public sealed class EventsDeliveryAdapter(IOutboxDeliveryReader outbox) : IEventDeliveryReader
{
    public async Task<IReadOnlyList<EventDeliveryRecord>> ListForAlertsAsync(
        Guid userId, IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default)
    {
        var rows = await outbox.ListForAlertsAsync(userId, alertIds, ct);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<EventDeliveryRecord?> FindAsync(Guid userId, Guid eventId, CancellationToken ct = default)
    {
        var row = await outbox.FindAsync(userId, eventId, ct);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<EventDeliveryRecord>> ListForDateAsync(
        Guid userId, DateOnly date, CancellationToken ct = default)
    {
        var rows = await outbox.ListForDateAsync(userId, date, ct);
        return rows.Select(ToRecord).ToList();
    }

    private static EventDeliveryRecord ToRecord(OutboxDeliveryRecord r)
        => new(r.EventId, r.AlertId, r.Kind, r.Subject, r.Disposition, r.OccurredAt, r.DispatchedAt, r.DeliveredAt);
}
