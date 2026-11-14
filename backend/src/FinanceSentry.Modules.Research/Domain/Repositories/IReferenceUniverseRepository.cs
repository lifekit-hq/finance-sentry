namespace FinanceSentry.Modules.Research.Domain.Repositories;

using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;

/// <summary>A list and how many tickers it holds.</summary>
public sealed record ReferenceListCount(string ListKey, string Source, int MemberCount);

/// <summary>Store of the reference lists and the log of the refreshes that maintain them.</summary>
public interface IReferenceUniverseRepository
{
    /// <summary>Member rows (list x ticker) the dataset currently holds.</summary>
    Task<int> CountBySourceAsync(string source, CancellationToken ct = default);

    /// <summary>
    /// Makes the dataset's stored rows equal <paramref name="desired"/> in one save: rows no longer wanted are
    /// removed, new ones added, a changed name updated. Returns the row count after the swap.
    /// </summary>
    Task<int> ReplaceSourceAsync(
        string source, IReadOnlyCollection<ReferenceListMember> desired, CancellationToken ct = default);

    Task AddRunAsync(ReferenceRefreshRun run, CancellationToken ct = default);

    Task<IReadOnlyList<ReferenceListCount>> ListListsAsync(CancellationToken ct = default);

    /// <summary>Members of an exact list key, ordered by ticker.</summary>
    Task<IReadOnlyList<ReferenceListMember>> ListMembersAsync(string listKey, CancellationToken ct = default);

    /// <summary>Exact list keys that contain the (normalised) ticker, ordered.</summary>
    Task<IReadOnlyList<string>> ListKeysForTickerAsync(string ticker, CancellationToken ct = default);

    /// <summary>The newest refresh attempts, newest first.</summary>
    Task<IReadOnlyList<ReferenceRefreshRun>> ListRecentRunsAsync(int take, CancellationToken ct = default);
}
