namespace FinanceSentry.Modules.Research.Domain.Repositories;

using FinanceSentry.Modules.Research.Domain;

public interface INewsSourceRepository
{
    Task<IReadOnlyList<NewsSource>> ListEnabledAsync(CancellationToken ct = default);

    /// <summary>
    /// Sources the health tracker has auto-retired for failing to fetch — excludes sources retired for
    /// thesis staleness (<see cref="NewsSource.RetiredReason"/> set), since those need no re-probing and
    /// must not be revived just because their feed still fetches (<c>NewsSourceRecoveryJob</c>).
    /// </summary>
    Task<IReadOnlyList<NewsSource>> ListDisabledAsync(CancellationToken ct = default);

    Task<IReadOnlyList<NewsSource>> ListAllAsync(CancellationToken ct = default);

    /// <summary>Sources registered to a specific thesis — retirement cleanup only.</summary>
    Task<IReadOnlyList<NewsSource>> ListByThesisAsync(Guid thesisId, CancellationToken ct = default);

    Task<NewsSource?> GetByUrlAsync(string url, CancellationToken ct = default);

    Task<Guid> AddAsync(NewsSource source, CancellationToken ct = default);

    Task UpdateAsync(NewsSource source, CancellationToken ct = default);

    /// <summary>Drops a source row outright — used to retire a superseded duplicate (issue #318).</summary>
    Task RemoveAsync(NewsSource source, CancellationToken ct = default);
}
