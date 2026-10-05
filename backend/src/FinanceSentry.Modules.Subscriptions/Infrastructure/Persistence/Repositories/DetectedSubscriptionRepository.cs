namespace FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// Reads run under SubscriptionsDbContext's Owner query filter. The detection job and the cross-module readers run
// with no person in scope, so they call the …Unscoped… methods, which opt out of the filter and keep their own
// UserId predicate. The upsert's existence check opts out too: with no person in scope a filtered check finds
// nothing and re-adds a row that already exists.
public class DetectedSubscriptionRepository(SubscriptionsDbContext db) : IDetectedSubscriptionRepository
{
    private readonly SubscriptionsDbContext _db = db;

    private IQueryable<DetectedSubscription> AllUsers => _db.DetectedSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name]);

    public async Task<IReadOnlyList<DetectedSubscription>> GetByUserIdAsync(
        string userId, bool includeDismissed, CancellationToken ct = default)
    {
        var query = _db.DetectedSubscriptions.AsNoTracking()
            .Where(s => s.UserId == userId);

        if (!includeDismissed)
            query = query.Where(s => s.Status != SubscriptionStatus.Dismissed);

        return await query.OrderBy(s => s.MerchantNameDisplay).ToListAsync(ct);
    }

    public Task<DetectedSubscription?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.DetectedSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<DetectedSubscription?> FindByUserAndMerchantUnscopedAsync(
        string userId, string merchantNameNormalized, CancellationToken ct = default)
        => AllUsers
            .FirstOrDefaultAsync(s => s.UserId == userId && s.MerchantNameNormalized == merchantNameNormalized, ct);

    public async Task UpsertAsync(DetectedSubscription subscription, CancellationToken ct = default)
    {
        var existing = await AllUsers
            .FirstOrDefaultAsync(s => s.Id == subscription.Id, ct);

        if (existing is null)
            _db.DetectedSubscriptions.Add(subscription);

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(DetectedSubscription subscription, CancellationToken ct = default)
    {
        _db.DetectedSubscriptions.Remove(subscription);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DetectedSubscription>> GetActiveByUserIdAsync(
        string userId, CancellationToken ct = default)
    {
        return await _db.DetectedSubscriptions.AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == SubscriptionStatus.Active)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DetectedSubscription>> GetActiveByUserIdUnscopedAsync(
        string userId, CancellationToken ct = default)
    {
        return await AllUsers.AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == SubscriptionStatus.Active)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DetectedSubscription>> GetStaleActiveUnscopedAsync(
        string userId, CancellationToken ct = default)
    {
        return await AllUsers
            .Where(s => s.UserId == userId && s.Status == SubscriptionStatus.Active)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DetectedSubscription>> GetLiveManualUnscopedAsync(
        string userId, CancellationToken ct = default)
    {
        return await AllUsers
            .Where(s => s.UserId == userId
                     && s.IsManual
                     && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PotentiallyCancelled))
            .ToListAsync(ct);
    }
}
