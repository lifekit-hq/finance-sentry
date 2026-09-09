namespace FinanceSentry.Modules.Subscriptions.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class SubscriptionHygieneSummaryReader(SubscriptionsDbContext db) : ISubscriptionHygieneSummaryReader
{
    private readonly SubscriptionsDbContext _db = db;

    public async Task<IReadOnlyList<SubscriptionHygieneSummary>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await _db.DetectedSubscriptions
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
                s.Kind,
                s.PreviousAmount))
            .ToListAsync(ct);
    }
}
