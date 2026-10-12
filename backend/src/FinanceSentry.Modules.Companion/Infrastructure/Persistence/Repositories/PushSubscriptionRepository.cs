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

    // Eviction, hand-over cleanup and the write run in one transaction. The write is one INSERT ... ON CONFLICT on the
    // unique Endpoint index, so two registrations of one device update one row instead of the loser failing on the
    // index; the conflict moves the row to the new owner and clears its failure state (the Id and CreatedAt stay).
    public async Task<PushSubscription> UpsertByEndpointAsync(PushSubscription subscription, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var owned = await db.PushSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(s => s.UserId == subscription.UserId)
            .OrderBy(s => s.CreatedAt)
            .Select(s => new { s.Id, s.Endpoint })
            .ToListAsync(ct);
        if (owned.All(s => s.Endpoint != subscription.Endpoint))
        {
            var evicted = owned.Take(owned.Count - (PushSubscriptionLimits.MaxPerUser - 1)).Select(s => s.Id).ToList();
            await db.PushSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name])
                .Where(s => evicted.Contains(s.Id))
                .ExecuteDeleteAsync(ct);
        }

        // Deliveries queued for the previous owner must not leak to the new one.
        var existingId = await db.PushSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(s => s.Endpoint == subscription.Endpoint)
            .Select(s => (Guid?)s.Id)
            .SingleOrDefaultAsync(ct);
        if (existingId is not null)
            await db.PushDeliveries.IgnoreQueryFilters([OwnerQueryFilter.Name])
                .Where(d => d.SubscriptionId == existingId && d.UserId != subscription.UserId)
                .ExecuteDeleteAsync(ct);

        var ids = await db.Database.SqlQuery<Guid>(
            $"""
            INSERT INTO companion.push_subscriptions
                ("Id", "UserId", "Endpoint", "P256dh", "Auth", "DeviceLabel", "CreatedAt", "FailureCount")
            VALUES
                ({subscription.Id}, {subscription.UserId}, {subscription.Endpoint}, {subscription.P256dh},
                 {subscription.Auth}, {subscription.DeviceLabel}, {subscription.CreatedAt.ToUniversalTime()}, 0)
            ON CONFLICT ("Endpoint") DO UPDATE SET
                "UserId" = EXCLUDED."UserId",
                "P256dh" = EXCLUDED."P256dh",
                "Auth" = EXCLUDED."Auth",
                "DeviceLabel" = EXCLUDED."DeviceLabel",
                "FailureCount" = 0,
                "LastFailureAt" = NULL,
                "DisabledAt" = NULL
            RETURNING "Id" AS "Value"
            """).ToListAsync(ct);
        var id = ids.Single();
        var saved = await db.PushSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .SingleAsync(s => s.Id == id, ct);

        await transaction.CommitAsync(ct);
        return saved;
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid id, CancellationToken ct = default)
        => await db.PushSubscriptions.Where(s => s.Id == id && s.UserId == userId).ExecuteDeleteAsync(ct) > 0;
}
