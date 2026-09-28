namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Modules.Wealth.Domain.Ports;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary><see cref="IBrokerageValueHistoryReader"/> impl over the internal net-worth snapshot repository.</summary>
public sealed class BrokerageValueHistoryReader(INetWorthSnapshotRepository snapshots) : IBrokerageValueHistoryReader
{
    public async Task<IReadOnlyList<DailyBrokerageValue>> GetDailyAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var raw = await snapshots.GetByUserIdAsync(userId, from, to, ct);
        return raw
            .Select(s => new DailyBrokerageValue(s.SnapshotDate, s.BrokerageTotal))
            .ToList();
    }
}
