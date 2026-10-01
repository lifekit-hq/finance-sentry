namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>Core-facing read of the watchlist for the Radar universe, over the internal repository.</summary>
public sealed class WatchlistReader(IWatchlistRepository watchlist) : IWatchlistReader
{
    public async Task<IReadOnlyList<string>> ListTickersAsync(Guid userId, CancellationToken ct = default)
    {
        var items = await watchlist.ListUnscopedAsync(userId, ct);
        return items
            .Select(i => i.Ticker.Trim().ToUpperInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
    }
}
