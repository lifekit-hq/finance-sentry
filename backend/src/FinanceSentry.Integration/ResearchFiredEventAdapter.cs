namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Companion.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// #1069 - <see cref="IFiredEventSource"/> over the Companion module's published <see cref="IOutboxDeliveryReader"/>
/// port: a Hold recommendation must answer a fired event that exists and belongs to the user.
/// </summary>
public sealed class ResearchFiredEventAdapter(IOutboxDeliveryReader outbox) : IFiredEventSource
{
    public async Task<FiredEventReference?> FindAsync(Guid userId, Guid eventId, CancellationToken ct = default)
    {
        var row = await outbox.FindAsync(userId, eventId, ct);
        return row is null ? null : new FiredEventReference(row.EventId, row.Kind, row.Subject, row.OccurredAt);
    }
}
