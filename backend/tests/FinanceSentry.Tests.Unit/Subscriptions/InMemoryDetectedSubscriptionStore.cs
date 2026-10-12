namespace FinanceSentry.Tests.Unit.Subscriptions;

using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// The subscription store on the in-memory provider. The repository's upsert is a raw <c>INSERT ... ON CONFLICT</c>,
/// which only Postgres runs (covered in <c>DetectedSubscriptionUpsertTests</c>); here it is a plain tracked write and
/// every read delegates to the real repository.
/// </summary>
internal sealed class InMemoryDetectedSubscriptionStore(SubscriptionsDbContext db) : IDetectedSubscriptionRepository
{
    private readonly DetectedSubscriptionRepository _repository = new(db);

    public Task<IReadOnlyList<DetectedSubscription>> GetByUserIdAsync(
        string userId, bool includeDismissed, CancellationToken ct = default)
        => _repository.GetByUserIdAsync(userId, includeDismissed, ct);

    public Task<DetectedSubscription?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _repository.GetByIdAsync(id, ct);

    public Task<DetectedSubscription?> FindByUserAndMerchantUnscopedAsync(
        string userId, string merchantNameNormalized, CancellationToken ct = default)
        => _repository.FindByUserAndMerchantUnscopedAsync(userId, merchantNameNormalized, ct);

    public async Task UpsertAsync(DetectedSubscription subscription, CancellationToken ct = default)
    {
        if (db.Entry(subscription).State == EntityState.Detached)
            db.DetectedSubscriptions.Add(subscription);

        await db.SaveChangesAsync(ct);
    }

    public Task DeleteAsync(DetectedSubscription subscription, CancellationToken ct = default)
        => _repository.DeleteAsync(subscription, ct);

    public Task<IReadOnlyList<DetectedSubscription>> GetActiveByUserIdAsync(string userId, CancellationToken ct = default)
        => _repository.GetActiveByUserIdAsync(userId, ct);

    public Task<IReadOnlyList<DetectedSubscription>> GetActiveByUserIdUnscopedAsync(string userId, CancellationToken ct = default)
        => _repository.GetActiveByUserIdUnscopedAsync(userId, ct);

    public Task<IReadOnlyList<DetectedSubscription>> GetStaleActiveUnscopedAsync(string userId, CancellationToken ct = default)
        => _repository.GetStaleActiveUnscopedAsync(userId, ct);

    public Task<IReadOnlyList<DetectedSubscription>> GetLiveManualUnscopedAsync(string userId, CancellationToken ct = default)
        => _repository.GetLiveManualUnscopedAsync(userId, ct);
}
