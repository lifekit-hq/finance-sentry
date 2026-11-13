namespace FinanceSentry.Modules.Research.Domain.Repositories;

using FinanceSentry.Modules.Research.Domain.Coverage;

/// <summary>
/// Store of the coverage universe, its journal and the verdict readings the rules decide from. The evaluation job
/// runs with no person in scope, so every read here opts out of the Owner query filter and keeps its own <c>UserId</c>
/// predicate (a request passes its resolved identity).
/// </summary>
public interface ICoverageRepository
{
    /// <summary>The user's members, ordered by ticker, for the job, the cross-module reader and the reads. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<CoverageMember>> ListMembersUnscopedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Every user with at least one member, for the evaluation sweep. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<Guid>> ListUserIdsUnscopedAsync(CancellationToken ct = default);

    /// <summary>
    /// Writes one evaluation's result in a single save: members to add, members to update (tracked or detached), members
    /// to remove, and the journal rows that explain each add and remove. Opts out of the Owner query filter.
    /// </summary>
    Task SaveEvaluationAsync(
        Guid userId,
        IReadOnlyCollection<CoverageMember> added,
        IReadOnlyCollection<CoverageMember> updated,
        IReadOnlyCollection<CoverageMember> removed,
        IReadOnlyCollection<CoverageJournalEntry> journal,
        CancellationToken ct = default);

    /// <summary>The user's newest journal rows for a ticker, newest first. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<CoverageJournalEntry>> ListJournalUnscopedAsync(
        Guid userId, string? ticker, int take, CancellationToken ct = default);

    /// <summary>When the journal last recorded a removal, per ticker (only tickers that were removed). Opts out of the Owner query filter.</summary>
    Task<IReadOnlyDictionary<string, DateTimeOffset>> ListLastRemovalsUnscopedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's verdict readings since <paramref name="since"/> (inclusive), ascending by day. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<CoverageVerdictObservation>> ListObservationsUnscopedAsync(
        Guid userId, DateOnly since, CancellationToken ct = default);

    /// <summary>
    /// Records a verdict reading unless the user already has one for that ticker and day; returns false when it already
    /// existed. Opts out of the Owner query filter.
    /// </summary>
    Task<bool> AddObservationUnscopedAsync(CoverageVerdictObservation observation, CancellationToken ct = default);

    /// <summary>Deletes verdict readings older than <paramref name="before"/> for every user. Opts out of the Owner query filter.</summary>
    Task<int> PruneObservationsUnscopedAsync(DateOnly before, CancellationToken ct = default);
}
