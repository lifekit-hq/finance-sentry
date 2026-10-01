namespace FinanceSentry.Modules.Subscriptions.Domain.Repositories;

public interface IDetectedSubscriptionRepository
{
    Task<IReadOnlyList<DetectedSubscription>> GetByUserIdAsync(
        string userId, bool includeDismissed, CancellationToken ct = default);

    Task<DetectedSubscription?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>The user's subscription for a merchant, for the detection job, which has no person in scope. Opts out of the Owner query filter.</summary>
    Task<DetectedSubscription?> FindByUserAndMerchantUnscopedAsync(
        string userId, string merchantNameNormalized, CancellationToken ct = default);

    Task UpsertAsync(DetectedSubscription subscription, CancellationToken ct = default);

    Task DeleteAsync(DetectedSubscription subscription, CancellationToken ct = default);

    Task<IReadOnlyList<DetectedSubscription>> GetActiveByUserIdAsync(
        string userId, CancellationToken ct = default);

    /// <summary>The user's active subscriptions for the cross-module reader, whose callers include jobs with no person in scope. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<DetectedSubscription>> GetActiveByUserIdUnscopedAsync(
        string userId, CancellationToken ct = default);

    /// <summary>The user's active subscriptions, tracked, for the detection job's staleness sweep. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<DetectedSubscription>> GetStaleActiveUnscopedAsync(
        string userId, CancellationToken ct = default);
}
