namespace FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;

using FinanceSentry.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

/// <summary>Erases the person's detected subscriptions and their price changes, keyed by the person's id in its string form.</summary>
internal sealed class SubscriptionsUserDataEraser(SubscriptionsDbContext db) : IUserDataEraser
{
    public string Module => "Subscriptions";

    public Task<int> EraseAsync(Guid userId, CancellationToken ct = default)
    {
        var key = userId.ToString();
        return EraseAsync(key, ct);
    }

    private async Task<int> EraseAsync(string key, CancellationToken ct)
    {
        var changes = await db.SubscriptionPriceChanges.IgnoreQueryFilters().Where(c => c.UserId == key).ExecuteDeleteAsync(ct);
        var subscriptions = await db.DetectedSubscriptions.IgnoreQueryFilters().Where(s => s.UserId == key).ExecuteDeleteAsync(ct);
        return changes + subscriptions;
    }
}
