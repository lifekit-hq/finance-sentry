namespace FinanceSentry.Modules.Research.Domain.Repositories;

using FinanceSentry.Modules.Research.Domain.Coverage;

/// <summary>
/// Store of the stage 6 research memos. The memo job runs with no person in scope, so every read here opts out of the
/// Owner query filter and keeps its own <c>UserId</c> predicate where it names a user.
/// </summary>
public interface ICoverageMemoRepository
{
    /// <summary>
    /// The user's ranked coverage adds since <paramref name="since"/> that have no memo yet and whose ticker is still a
    /// member (a held or watchlisted add is the owner's own act, not an entrant), best rank first, then oldest.
    /// </summary>
    Task<IReadOnlyList<CoverageJournalEntry>> ListEntrantsWithoutMemoUnscopedAsync(
        Guid userId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Memos written at or after <paramref name="since"/> across every user, and what they cost (USD).</summary>
    Task<(int Count, decimal CostUsd)> TotalsSinceUnscopedAsync(DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Stores the memo; returns false when the user already has one for that journal entry. Opts out of the Owner query filter.</summary>
    Task<bool> AddUnscopedAsync(CoverageMemo memo, CancellationToken ct = default);

    /// <summary>The user's newest memo for the ticker, or null. Opts out of the Owner query filter.</summary>
    Task<CoverageMemo?> GetLatestUnscopedAsync(Guid userId, string ticker, CancellationToken ct = default);

    /// <summary>When the user's newest memo per ticker was written. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyDictionary<string, DateTimeOffset>> LatestTimesUnscopedAsync(Guid userId, CancellationToken ct = default);
}
