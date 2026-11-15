namespace FinanceSentry.Modules.Research.Domain.Repositories;

using FinanceSentry.Modules.Research.Domain.Revisions;

public interface IThesisRevisionRepository
{
    Task<ThesisRevision?> GetAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// The pending proposal on the subject, tracked so a supersede can save it. It may be past its expiry but not yet
    /// swept: the unique index still counts it, so the caller settles it rather than ignoring it.
    /// </summary>
    Task<ThesisRevision?> FindPendingAsync(Guid userId, Guid subjectId, CancellationToken ct = default);

    /// <summary>Live (not yet expired) pending proposals, oldest first.</summary>
    Task<IReadOnlyList<ThesisRevision>> ListPendingAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Proposals that left Pending on or after <paramref name="since"/> (decided, expired or superseded), newest first.</summary>
    Task<IReadOnlyList<ThesisRevision>> ListClosedSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Every user's pending proposals past their expiry. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<ThesisRevision>> ListExpiredUnscopedAsync(DateTimeOffset asOf, CancellationToken ct = default);

    /// <summary>
    /// Inserts a pending proposal. Throws <see cref="Exceptions.ThesisRevisionConflictException"/> when another pending
    /// proposal on the subject landed first (the partial unique index) or when <paramref name="supersedes"/> is no longer
    /// pending (a decision claimed it). <paramref name="supersedes"/>, an already-changed row, is written in the same
    /// transaction as a conditional update, so a failed insert leaves it as it was.
    /// </summary>
    Task AddPendingAsync(ThesisRevision revision, ThesisRevision? supersedes = null, CancellationToken ct = default);

    /// <summary>
    /// Atomically moves a proposal that is still pending and unexpired to <paramref name="status"/>, stamping the decision.
    /// False when another decision (or the expiry) got there first.
    /// </summary>
    Task<bool> TryClaimAsync(
        Guid userId, Guid id, ThesisRevisionStatus status, string? note, DateTimeOffset decidedAt, CancellationToken ct = default);

    /// <summary>
    /// Puts a claimed (Confirmed) proposal back to Pending when the confirm wrote nothing. When a newer proposal took the
    /// subject meanwhile, the claimed one is marked Superseded instead. Returns the status it ends in.
    /// </summary>
    Task<ThesisRevisionStatus> ReleaseClaimAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>Saves a changed row. The expiry job writes with no person in scope; SaveChanges is not filtered.</summary>
    Task UpdateAsync(ThesisRevision revision, CancellationToken ct = default);
}
