namespace FinanceSentry.Modules.Research.Domain.Repositories;

public interface IWatchlistRepository
{
    Task<IReadOnlyList<WatchlistItem>> ListAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's watchlist for the cross-module reader, which jobs call with no person in scope. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<WatchlistItem>> ListUnscopedAsync(Guid userId, CancellationToken ct = default);

    Task<WatchlistItem?> FindAsync(Guid userId, string ticker, CancellationToken ct = default);

    Task AddAsync(WatchlistItem item, CancellationToken ct = default);

    Task<bool> RemoveAsync(Guid userId, Guid itemId, CancellationToken ct = default);
}
