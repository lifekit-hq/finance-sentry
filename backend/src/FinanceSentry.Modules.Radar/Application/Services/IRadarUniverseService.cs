namespace FinanceSentry.Modules.Radar.Application.Services;

using FinanceSentry.Modules.Radar.Domain;

public interface IRadarUniverseService
{
    /// <summary>
    /// Resolves the universe = seed ∪ equity holdings ∪ watchlist ∪ thesis tickers, persists membership
    /// (upsert + reactivate), and de-activates tickers that have left. Returns the active members.
    /// </summary>
    Task<IReadOnlyList<RadarUniverseMember>> SyncAsync(CancellationToken ct = default);

    /// <summary>Current active universe members without re-syncing (reads never mutate).</summary>
    Task<IReadOnlyList<RadarUniverseMember>> GetActiveAsync(CancellationToken ct = default);
}
