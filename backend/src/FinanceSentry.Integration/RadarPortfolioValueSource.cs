namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Wealth.Domain.Ports;

/// <summary>
/// 412: implements the Radar module's <see cref="IPortfolioValueSource"/> by reading daily
/// brokerage-sleeve totals through the Wealth module's published <see cref="IBrokerageValueHistoryReader"/>
/// port (#673). Lives in the Integration layer so neither module references the other directly.
/// </summary>
public sealed class RadarPortfolioValueSource(IBrokerageValueHistoryReader brokerageHistory)
    : IPortfolioValueSource
{
    public async Task<IReadOnlyList<DailyPortfolioValue>> GetAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var daily = await brokerageHistory.GetDailyAsync(userId, from, to, ct);
        return daily
            .Select(d => new DailyPortfolioValue(d.Date, d.BrokerageTotalUsd))
            .ToList();
    }
}
