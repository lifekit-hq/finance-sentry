namespace FinanceSentry.Modules.Subscriptions.Application.Services;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Implements <see cref="ISubscriptionHygieneSummaryReader"/> over every user's active subscriptions. Its caller is the
/// price-hike detection job, which has no person in scope, so it opts out of the Owner query filter.
/// </summary>
public class SubscriptionHygieneSummaryReader(SubscriptionsDbContext db) : ISubscriptionHygieneSummaryReader
{
    private readonly SubscriptionsDbContext _db = db;

    public async Task<IReadOnlyList<SubscriptionHygieneSummary>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await _db.DetectedSubscriptions
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active)
            .Select(s => new SubscriptionHygieneSummary(
                s.Id,
                // DetectedSubscription.UserId is string; convert at the adapter boundary.
                Guid.Parse(s.UserId),
                s.MerchantNameDisplay,
                s.AverageAmount,
                s.LastKnownAmount,
                s.Currency,
                s.OccurrenceCount,
                s.PreviousAmount))
            .ToListAsync(ct);
    }
}
