namespace FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class PushSubscriptionRepository(CompanionDbContext db) : IPushSubscriptionRepository
{
    public async Task<IReadOnlyList<PushSubscription>> ListAsync(Guid userId, CancellationToken ct = default)
        => await db.PushSubscriptions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<PushSubscription> UpsertByEndpointAsync(PushSubscription subscription, CancellationToken ct = default)
    {
        var existing = await db.PushSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .FirstOrDefaultAsync(s => s.Endpoint == subscription.Endpoint, ct);
        if (existing is null || existing.UserId != subscription.UserId)
        {
            var owned = await db.PushSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name])
                .Where(s => s.UserId == subscription.UserId)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync(ct);
            db.PushSubscriptions.RemoveRange(owned.Take(owned.Count - (PushSubscriptionLimits.MaxPerUser - 1)));
        }

        if (existing is null)
        {
            db.PushSubscriptions.Add(subscription);
            await db.SaveChangesAsync(ct);
            return subscription;
        }

        // Deliveries queued for the previous owner must not leak to the new one.
        if (existing.UserId != subscription.UserId)
            await db.PushDeliveries.IgnoreQueryFilters([OwnerQueryFilter.Name])
                .Where(d => d.SubscriptionId == existing.Id && d.UserId != subscription.UserId)
                .ExecuteDeleteAsync(ct);

        existing.UserId = subscription.UserId;
        existing.P256dh = subscription.P256dh;
        existing.Auth = subscription.Auth;
        existing.DeviceLabel = subscription.DeviceLabel;
        existing.FailureCount = 0;
        existing.LastFailureAt = null;
        existing.DisabledAt = null;
        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid id, CancellationToken ct = default)
        => await db.PushSubscriptions.Where(s => s.Id == id && s.UserId == userId).ExecuteDeleteAsync(ct) > 0;
}
