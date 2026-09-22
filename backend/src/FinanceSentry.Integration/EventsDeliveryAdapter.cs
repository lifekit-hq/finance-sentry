namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FinanceSentry.Modules.Events.Domain.Ports;

/// <summary>
/// Feature 049 - <see cref="IEventDeliveryReader"/> over the Companion outbox. An alert's outbox row
/// is found by the dedup key the capture wrote (<see cref="IMaterialityPolicy.AlertDedupKey"/>), and
/// the alert id is read back from the same key. Dispositions cross as their enum names.
/// </summary>
public sealed class EventsDeliveryAdapter(
    ICompanionEventRepository events,
    IMaterialityPolicy policy) : IEventDeliveryReader
{
    public async Task<IReadOnlyList<EventDeliveryRecord>> ListForAlertsAsync(
        Guid userId, IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default)
    {
        if (alertIds.Count == 0)
        {
            return [];
        }

        var keys = alertIds.Distinct().Select(policy.AlertDedupKey).ToList();
        var rows = await events.ListByDedupKeysAsync(userId, keys, ct);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<EventDeliveryRecord?> FindAsync(Guid userId, Guid eventId, CancellationToken ct = default)
    {
        var evt = await events.GetAsync(eventId, ct);
        return evt is null || evt.UserId != userId ? null : ToRecord(evt);
    }

    private EventDeliveryRecord ToRecord(CompanionEvent e)
        => new(e.Id, policy.AlertIdFromDedupKey(e.DedupKey), e.Kind.ToString(), e.Disposition.ToString(), e.OccurredAt, e.DispatchedAt, e.DeliveredAt);
}
