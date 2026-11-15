namespace FinanceSentry.Modules.Research.Domain.Repositories;

using FinanceSentry.Modules.Research.Domain.Revisions;

public interface IThesisRevisionRepository
{
    Task<ThesisRevision?> GetAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>The live (not yet expired) pending proposal on the subject, tracked so a supersede can save it.</summary>
    Task<ThesisRevision?> FindPendingAsync(Guid userId, Guid subjectId, CancellationToken ct = default);

    /// <summary>Live (not yet expired) pending proposals, oldest first.</summary>
    Task<IReadOnlyList<ThesisRevision>> ListPendingAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Proposals that left Pending on or after <paramref name="since"/> (decided, expired or superseded), newest first.</summary>
    Task<IReadOnlyList<ThesisRevision>> ListClosedSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Every user's pending proposals past their expiry. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<ThesisRevision>> ListExpiredUnscopedAsync(DateTimeOffset asOf, CancellationToken ct = default);

    /// <summary>
    /// Inserts a pending proposal. Throws <see cref="Exceptions.ThesisRevisionConflictException"/> when another pending
    /// proposal on the subject landed first (the partial unique index). <paramref name="supersedes"/>, an already-changed
    /// tracked row, is saved in the same transaction so a failed insert leaves it as it was.
    /// </summary>
    Task AddPendingAsync(ThesisRevision revision, ThesisRevision? supersedes = null, CancellationToken ct = default);

    /// <summary>Saves a changed row. The expiry job writes with no person in scope; SaveChanges is not filtered.</summary>
    Task UpdateAsync(ThesisRevision revision, CancellationToken ct = default);
}
