namespace FinanceSentry.Modules.Companion.Domain.Repositories;

using FinanceSentry.Modules.Companion.Domain;

public interface IPushSubscriptionRepository
{
    /// <summary>The user's subscriptions, oldest first.</summary>
    Task<IReadOnlyList<PushSubscription>> ListAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Insert the subscription, or move an existing row with the same endpoint to its new owner and refresh
    /// its keys. A device that changes signed-in user would otherwise hit the unique endpoint index, so the lookup opts
    /// out of the Owner query filter.</summary>
    Task<PushSubscription> UpsertByEndpointAsync(PushSubscription subscription, CancellationToken ct = default);

    /// <summary>Delete one of the user's subscriptions; false when it does not exist or belongs to someone else.</summary>
    Task<bool> RemoveAsync(Guid userId, Guid id, CancellationToken ct = default);
}
