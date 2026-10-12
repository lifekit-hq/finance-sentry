namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Wealth.Domain.Ports;

/// <summary>
/// 412: implements the Radar module's <see cref="IPortfolioValueSource"/> by reading daily
/// invested brokerage values (broker cash excluded) through the Wealth module's published <see cref="IBrokerageValueHistoryReader"/>
/// port (#673). Lives in the Integration layer so neither module references the other directly.
/// </summary>
public sealed class RadarPortfolioValueSource(IBrokerageValueHistoryReader brokerageHistory)
    : IPortfolioValueSource
{
    public async Task<IReadOnlyList<DailyPortfolioValue>> GetAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var daily = await brokerageHistory.GetDailyAsync(userId, from, to, ct);
        // A day with no split is skipped: neither zero nor the cash-inclusive total stands in for it.
        return daily
            .Where(d => d.BrokerageInvestedUsd is not null)
            .Select(d => new DailyPortfolioValue(d.Date, d.BrokerageInvestedUsd!.Value))
            .ToList();
    }
}
