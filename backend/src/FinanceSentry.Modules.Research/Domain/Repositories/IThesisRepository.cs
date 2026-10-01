namespace FinanceSentry.Modules.Research.Domain.Repositories;

public interface IThesisRepository
{
    Task<IReadOnlyList<InvestmentThesis>> ListAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's theses for jobs, cross-module readers and the handlers they share, which can run with no person in scope. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<InvestmentThesis>> ListUnscopedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Every user with a thesis, for the job sweeps. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<Guid>> GetUserIdsWithThesesUnscopedAsync(CancellationToken ct = default);

    Task<InvestmentThesis?> FindAsync(Guid userId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<InvestmentThesis>> FindByTickerAsync(Guid userId, string ticker, CancellationToken ct = default);

    /// <summary>Inserts or updates the thesis. The existence check opts out of the Owner query filter, so the monitor job updates rather than re-inserts.</summary>
    Task UpsertAsync(InvestmentThesis thesis, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
}
