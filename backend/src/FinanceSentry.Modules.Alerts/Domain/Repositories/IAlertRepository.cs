namespace FinanceSentry.Modules.Alerts.Domain.Repositories;

public interface IAlertRepository
{
    Task<(IReadOnlyList<Alert> Items, int TotalCount, int UnreadCount)> GetPagedAsync(
        Guid userId, string filter, int page, int pageSize, CancellationToken ct = default);

    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);

    Task<Alert?> FindActiveAsync(
        Guid userId, string type, Guid? referenceId, CancellationToken ct = default);

    /// <summary>
    /// Whether an alert of <paramref name="type"/> on <paramref name="referenceId"/> was ever raised
    /// for the user, whatever its state (open, read, dismissed or resolved).
    /// </summary>
    Task<bool> ExistsAsync(Guid userId, string type, Guid? referenceId, CancellationToken ct = default);

    Task<bool> HasRecentAsync(
        Guid userId, string type, Guid? referenceId, string? referenceLabel, DateTimeOffset createdAfter,
        CancellationToken ct = default);

    Task<bool> MarkReadAsync(Guid userId, Guid alertId, CancellationToken ct = default);

    Task MarkAllReadAsync(Guid userId, CancellationToken ct = default);

    Task<bool> DismissAsync(Guid userId, Guid alertId, CancellationToken ct = default);

    Task ResolveAsync(Guid alertId, CancellationToken ct = default);

    Task<int> PurgeOldAsync(DateTimeOffset olderThan, CancellationToken ct = default);

    Task DeleteByReferenceIdAsync(Guid referenceId, CancellationToken ct = default);

    Task AddAsync(Alert alert, CancellationToken ct = default);

    /// <summary>
    /// Records the user's one-tap decision ("Accept" or "Defer") against the alert. If the decision
    /// is "Accept" the alert is also resolved so it no longer appears as open. Returns false when the
    /// alert is not found or does not belong to the user.
    /// </summary>
    Task<bool> AcknowledgeAsync(Guid userId, Guid alertId, string decision, CancellationToken ct = default);

    /// <summary>
    /// Same as <see cref="AcknowledgeAsync"/> but looks up the active alert by <paramref name="referenceId"/>
    /// instead of its row id. Used by the bot's inline-keyboard callback which only knows the stable
    /// reference anchor from the wake payload.
    /// </summary>
    Task<bool> AcknowledgeByReferenceAsync(Guid userId, string alertType, Guid referenceId, string decision, CancellationToken ct = default);

    /// <summary>
    /// Non-dismissed alerts whose <c>Type</c> is in <paramref name="types"/>, newest first, paged
    /// (feature 049: the fired-events feed). Read-only; the generator and its dedup are untouched.
    /// </summary>
    Task<(IReadOnlyList<Alert> Items, int TotalCount)> GetByTypesPagedAsync(
        Guid userId, IReadOnlyCollection<string> types, int page, int pageSize, CancellationToken ct = default);
}
