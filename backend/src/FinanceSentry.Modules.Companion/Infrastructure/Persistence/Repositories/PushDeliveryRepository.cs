namespace FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class PushDeliveryRepository(CompanionDbContext db) : IPushDeliveryRepository
{
    public async Task<IReadOnlyList<PushSubscription>> ListActiveSubscriptionsUnscopedAsync(CancellationToken ct = default)
    {
        var optedIn = db.NotificationSettings.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(n => n.PushEnabled)
            .Select(n => n.UserId);
        return await db.PushSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(s => s.DisabledAt == null && optedIn.Contains(s.UserId))
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CompanionEvent>> ListUndeliveredEventsUnscopedAsync(
        Guid userId, Guid subscriptionId, DateTimeOffset since, bool includeOperational, int limit, CancellationToken ct = default)
    {
        var deliveries = db.PushDeliveries.IgnoreQueryFilters([OwnerQueryFilter.Name]);
        return await db.Events.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(e => e.UserId == userId
                        && e.CapturedAt >= since
                        && (includeOperational || e.Kind != CompanionEventKind.OperationalFailure)
                        && !deliveries.Any(d => d.EventId == e.Id && d.SubscriptionId == subscriptionId))
            .OrderBy(e => e.CapturedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task AddDeliveriesAsync(IReadOnlyCollection<PushDelivery> deliveries, CancellationToken ct = default)
    {
        foreach (var delivery in deliveries)
        {
            var exists = await db.PushDeliveries.IgnoreQueryFilters([OwnerQueryFilter.Name])
                .AnyAsync(d => d.EventId == delivery.EventId && d.SubscriptionId == delivery.SubscriptionId, ct);
            if (!exists)
                db.PushDeliveries.Add(delivery);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PushDelivery>> ListDueUnscopedAsync(DateTimeOffset now, int limit, CancellationToken ct = default)
    {
        var optedIn = db.NotificationSettings.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(n => n.PushEnabled)
            .Select(n => n.UserId);
        return await db.PushDeliveries.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(d => d.Status == PushDeliveryStatus.Pending
                        && (d.NextAttemptAt == null || d.NextAttemptAt <= now)
                        && optedIn.Contains(d.UserId))
            .OrderBy(d => d.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, CompanionEvent>> GetEventsUnscopedAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => await db.Events.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

    public async Task<IReadOnlyDictionary<Guid, PushSubscription>> GetSubscriptionsUnscopedAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => await db.PushSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

    public async Task RemoveSubscriptionUnscopedAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        await db.PushDeliveries.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(d => d.SubscriptionId == subscriptionId)
            .ExecuteDeleteAsync(ct);
        await db.PushSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(s => s.Id == subscriptionId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> PruneDisabledSubscriptionsUnscopedAsync(DateTimeOffset disabledBefore, CancellationToken ct = default)
        => await db.PushSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(s => s.DisabledAt != null && s.DisabledAt < disabledBefore)
            .ExecuteDeleteAsync(ct);

    public async Task SaveAsync(CancellationToken ct = default)
        => await db.SaveChangesAsync(ct);
}
