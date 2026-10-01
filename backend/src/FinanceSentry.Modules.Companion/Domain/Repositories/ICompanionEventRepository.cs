namespace FinanceSentry.Modules.Companion.Domain.Repositories;

using FinanceSentry.Modules.Companion.Domain;

public interface ICompanionEventRepository
{
    /// <summary>Insert only if no row with the same <c>DedupKey</c> exists. Returns true if inserted. The capture job
    /// runs with no person in scope, so the dedup check opts out of the Owner query filter.</summary>
    Task<bool> InsertIfNewAsync(CompanionEvent evt, CancellationToken ct = default);

    /// <summary>Events for a user in any of the given dispositions, newest first.</summary>
    Task<IReadOnlyList<CompanionEvent>> ListByDispositionAsync(
        Guid userId, IReadOnlyCollection<EventDisposition> dispositions, int limit, CancellationToken ct = default);

    /// <summary>All realtime-pending events across users, oldest first (relay input). Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<CompanionEvent>> ListRealtimePendingUnscopedAsync(int limit, CancellationToken ct = default);

    /// <summary>All held-for-digest events for a user, oldest first, for the digest job. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<CompanionEvent>> ListHeldForDigestUnscopedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Distinct user ids with at least one event currently held for the digest. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<Guid>> ListHeldForDigestUserIdsUnscopedAsync(CancellationToken ct = default);

    /// <summary>Count of proactive events dispatched for a user since a cutoff (rate limiting in the dispatch job).
    /// Opts out of the Owner query filter.</summary>
    Task<int> CountDispatchedSinceUnscopedAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default);

    Task<CompanionEvent?> GetAsync(Guid id, CancellationToken ct = default);

    Task UpdateAsync(CompanionEvent evt, CancellationToken ct = default);

    /// <summary>Mark the given events for a user as <see cref="EventDisposition.Delivered"/>.</summary>
    Task<int> MarkDeliveredAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>A user's events whose <c>DedupKey</c> is in the given set (feature 049: alert → outbox row lookup).</summary>
    Task<IReadOnlyList<CompanionEvent>> ListByDedupKeysAsync(
        Guid userId, IReadOnlyCollection<string> dedupKeys, CancellationToken ct = default);

    /// <summary>A user's events that occurred in [from, to), any kind or disposition (feature 687: the
    /// per-day outcome view - what fired regardless of whether it was ever delivered).</summary>
    Task<IReadOnlyList<CompanionEvent>> ListByOccurredRangeAsync(
        Guid userId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
