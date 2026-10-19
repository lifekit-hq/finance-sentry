namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Ports;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary><see cref="IBrokerageValueHistoryReader"/> impl over the internal net-worth snapshot repository.</summary>
public sealed class BrokerageValueHistoryReader(INetWorthSnapshotRepository snapshots) : IBrokerageValueHistoryReader
{
    private const string BrokerageSleeve = "brokerage";

    public async Task<IReadOnlyList<DailyBrokerageValue>> GetDailyAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var raw = await snapshots.GetByUserIdUnscopedAsync(userId, from, to, ct);
        var withSources = SnapshotSources.OfEach(
            raw, await snapshots.GetSourcesUnscopedAsync(userId, [.. raw.Select(s => s.Id)], ct));
        // Another source failing, carried forward or estimated does not touch the brokerage figure, so only the
        // brokerage source's own STALE_SOURCE reaches its signals.
        return withSources
            .Select(x => new DailyBrokerageValue(
                x.Snapshot.SnapshotDate, x.Snapshot.BrokerageTotal, x.Snapshot.BrokerageInvested,
                x.Sources.Signals(x.Snapshot) with
                {
                    StaleSources = x.Sources.IsStale(BrokerageSleeve) ? [BrokerageSleeve] : [],
                    EstimatedSources = [],
                }))
            .ToList();
    }
}
