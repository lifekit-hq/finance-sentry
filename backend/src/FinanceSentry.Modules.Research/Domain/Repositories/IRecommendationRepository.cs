namespace FinanceSentry.Modules.Research.Domain.Repositories;

using FinanceSentry.Modules.Research.Domain.Recommendations;

public interface IRecommendationRepository
{
    Task<Recommendation?> GetAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>The live (not yet expired) pending proposal on the ticker, untracked.</summary>
    Task<Recommendation?> FindPendingAsync(Guid userId, string ticker, CancellationToken ct = default);

    /// <summary>Live (not yet expired) pending proposals, oldest first.</summary>
    Task<IReadOnlyList<Recommendation>> ListPendingAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Proposals that left Pending on or after <paramref name="since"/> (decided, expired or superseded), newest first.</summary>
    Task<IReadOnlyList<Recommendation>> ListClosedSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>The pending proposal on the ticker, for the rule sweep. Opts out of the Owner query filter.</summary>
    Task<Recommendation?> FindPendingUnscopedAsync(Guid userId, string ticker, CancellationToken ct = default);

    /// <summary>
    /// Whether a proposal on the ticker for the same trigger was made since <paramref name="since"/>, in any status -
    /// the rule sweep's cooldown, so a rejected or expired proposal is not put again the next morning. Opts out of the
    /// Owner query filter.
    /// </summary>
    Task<bool> ExistsForTriggerSinceUnscopedAsync(
        Guid userId, string ticker, RecommendationTriggerKind kind, string triggerRef, DateTimeOffset since,
        CancellationToken ct = default);

    /// <summary>Every user's pending proposals past their expiry. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<Recommendation>> ListExpiredUnscopedAsync(DateTimeOffset asOf, CancellationToken ct = default);

    /// <summary>Every user's decided proposals whose outcome is still open. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<Recommendation>> ListOutcomeOpenUnscopedAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts a pending proposal. Throws <see cref="Exceptions.RecommendationConflictException"/> when another pending
    /// proposal on the ticker landed first (the partial unique index). <paramref name="supersedes"/>, an already-changed
    /// tracked row, is saved in the same transaction so a failed insert leaves it as it was.
    /// </summary>
    Task AddPendingAsync(Recommendation recommendation, Recommendation? supersedes = null, CancellationToken ct = default);

    /// <summary>Saves a changed row. Jobs write with no person in scope; SaveChanges is not filtered.</summary>
    Task UpdateAsync(Recommendation recommendation, CancellationToken ct = default);
}
