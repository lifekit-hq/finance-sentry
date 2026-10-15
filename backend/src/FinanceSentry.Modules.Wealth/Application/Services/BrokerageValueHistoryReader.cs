namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Core.Headlines;

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
        return raw
            .Select(s => new DailyBrokerageValue(
                s.SnapshotDate, s.BrokerageTotal, s.BrokerageInvested,
                HeadlineSignals.FromSnapshot(BrokerageOnly(s.StaleSleeves), s.IsApproximate, s.FxStatus)))
            .ToList();
    }

    // Another sleeve carried forward does not touch the brokerage figure.
    private static string? BrokerageOnly(string? staleSleeves) =>
        staleSleeves?.Split(',', StringSplitOptions.TrimEntries).Contains(BrokerageSleeve, StringComparer.OrdinalIgnoreCase) == true
            ? BrokerageSleeve
            : null;

}
