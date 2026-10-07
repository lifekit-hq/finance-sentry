namespace FinanceSentry.Modules.Companion.Domain.Repositories;

using FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// The push sender's reads and writes (spec 859). The sender runs with no person in scope, so every method opts out of
/// the Owner query filter and keeps its own <c>UserId</c> predicate. Entities come back tracked; <see cref="SaveAsync"/>
/// persists the changes the job made to them.
/// </summary>
public interface IPushDeliveryRepository
{
    /// <summary>Subscriptions that may receive pushes now: not soft-disabled, owner has opted in.</summary>
    Task<IReadOnlyList<PushSubscription>> ListActiveSubscriptionsUnscopedAsync(CancellationToken ct = default);

    /// <summary>The subscription's owner's events captured at or after <paramref name="since"/> that have no delivery row for
    /// this subscription yet, oldest first. <c>AnalystAction</c> events are left out: the deduped rating-change alert is pushed instead. <c>OperationalFailure</c> events are left out unless
    /// <paramref name="includeOperational"/>. Never reads or filters on <c>Disposition</c>.</summary>
    Task<IReadOnlyList<CompanionEvent>> ListUndeliveredEventsUnscopedAsync(
        Guid userId, Guid subscriptionId, DateTimeOffset since, bool includeOperational, int limit, CancellationToken ct = default);

    /// <summary>The subset of <paramref name="alertIds"/> that has no delivery row for <paramref name="subscriptionId"/> yet.</summary>
    Task<IReadOnlySet<Guid>> ListUndeliveredAlertIdsUnscopedAsync(
        Guid subscriptionId, IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default);

    /// <summary>Insert delivery rows; a row that already exists for (event or alert, subscription) is skipped, so an overlapping
    /// run never queues the same push twice.</summary>
    Task AddDeliveriesAsync(IReadOnlyCollection<PushDelivery> deliveries, CancellationToken ct = default);

    /// <summary>Pending deliveries whose next attempt is due, for opted-in owners, oldest first.</summary>
    Task<IReadOnlyList<PushDelivery>> ListDueUnscopedAsync(DateTimeOffset now, int limit, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, CompanionEvent>> GetEventsUnscopedAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, PushSubscription>> GetSubscriptionsUnscopedAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>Delete a subscription the push service reported gone; its deliveries go with it.</summary>
    Task RemoveSubscriptionUnscopedAsync(Guid subscriptionId, CancellationToken ct = default);

    /// <summary>Delete subscriptions soft-disabled before <paramref name="disabledBefore"/>.</summary>
    Task<int> PruneDisabledSubscriptionsUnscopedAsync(DateTimeOffset disabledBefore, CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);
}
