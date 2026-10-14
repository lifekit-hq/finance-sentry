namespace FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;

using FinanceSentry.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

/// <summary>Erases the person's detected subscriptions, keyed by the person's id in its string form.</summary>
internal sealed class SubscriptionsUserDataEraser(SubscriptionsDbContext db) : IUserDataEraser
{
    public string Module => "Subscriptions";

    public Task<int> EraseAsync(Guid userId, CancellationToken ct = default)
    {
        var key = userId.ToString();
        return db.DetectedSubscriptions.IgnoreQueryFilters().Where(s => s.UserId == key).ExecuteDeleteAsync(ct);
    }
}
