namespace FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// Reads run under SubscriptionsDbContext's Owner query filter. The detection job and the cross-module readers run
// with no person in scope, so they call the …Unscoped… methods, which opt out of the filter and keep their own
// UserId predicate. A new row is written by a single INSERT ... ON CONFLICT, which the Owner filter does not touch, so
// it works with no person in scope too.
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

    // A row this context tracks was loaded, so it exists: it is written back by its Id, which also carries a changed
    // key (a legacy row re-keyed when it is linked to a transaction). A new row is one INSERT ... ON CONFLICT on the
    // unique (UserId, MerchantNameNormalized) index: one already stored under that key (the detection job re-finds it
    // in a race) is updated in place with the caller's state, keeping its Id and DetectedAt, so two writers of one key
    // leave one row instead of the loser failing on the index, and no read precedes it.
    public async Task UpsertAsync(DetectedSubscription subscription, CancellationToken ct = default)
    {
        if (_db.Entry(subscription).State != EntityState.Detached)
        {
            await _db.SaveChangesAsync(ct);
            return;
        }

        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.detected_subscriptions
                ("Id", "UserId", "MerchantNameNormalized", "MerchantNameDisplay", "Cadence", "AverageAmount",
                 "LastKnownAmount", "PreviousAmount", "Currency", "Kind", "LastChargeDate", "NextExpectedDate", "Status",
                 "OccurrenceCount", "ConfidenceScore", "Category", "TermCount", "EndDate", "StartDate", "IsManual",
                 "DetectedAt", "UpdatedAt", "DismissedAt")
            VALUES
                ({subscription.Id}, {subscription.UserId}, {subscription.MerchantNameNormalized},
                 {subscription.MerchantNameDisplay}, {subscription.Cadence}, {subscription.AverageAmount},
                 {subscription.LastKnownAmount}, {subscription.PreviousAmount}, {subscription.Currency},
                 {subscription.Kind}, {subscription.LastChargeDate}, {subscription.NextExpectedDate},
                 {subscription.Status}, {subscription.OccurrenceCount}, {subscription.ConfidenceScore},
                 {subscription.Category}, {subscription.TermCount}, {subscription.EndDate}, {subscription.StartDate},
                 {subscription.IsManual}, {subscription.DetectedAt.ToUniversalTime()}, now(),
                 {subscription.DismissedAt?.ToUniversalTime()})
            ON CONFLICT ("UserId", "MerchantNameNormalized") DO UPDATE SET
                "MerchantNameDisplay" = EXCLUDED."MerchantNameDisplay",
                "Cadence" = EXCLUDED."Cadence",
                "AverageAmount" = EXCLUDED."AverageAmount",
                "LastKnownAmount" = EXCLUDED."LastKnownAmount",
                "PreviousAmount" = EXCLUDED."PreviousAmount",
                "Currency" = EXCLUDED."Currency",
                "Kind" = EXCLUDED."Kind",
                "LastChargeDate" = EXCLUDED."LastChargeDate",
                "NextExpectedDate" = EXCLUDED."NextExpectedDate",
                "Status" = EXCLUDED."Status",
                "OccurrenceCount" = EXCLUDED."OccurrenceCount",
                "ConfidenceScore" = EXCLUDED."ConfidenceScore",
                "Category" = EXCLUDED."Category",
                "TermCount" = EXCLUDED."TermCount",
                "EndDate" = EXCLUDED."EndDate",
                "StartDate" = EXCLUDED."StartDate",
                "IsManual" = EXCLUDED."IsManual",
                "UpdatedAt" = now(),
                "DismissedAt" = EXCLUDED."DismissedAt"
            """,
            ct);
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
