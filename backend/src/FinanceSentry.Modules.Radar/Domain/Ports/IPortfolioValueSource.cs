namespace FinanceSentry.Modules.Radar.Domain.Ports;

using FinanceSentry.Core.Headlines;

/// <summary>
/// Read-port: daily brokerage values for a user - the invested positions (idle broker cash excluded) and the
/// cash-inclusive total - sourced from net-worth snapshots.
/// Lives in Radar's domain so the BookPerformanceService can consume portfolio history without
/// a compile-time dependency on the Wealth module.
/// </summary>
public interface IPortfolioValueSource
{
    /// <summary>
    /// Daily brokerage values for the user from <paramref name="from"/> through <paramref name="to"/>,
    /// ordered oldest→newest. A snapshot day with no cash/invested split is kept with a null
    /// <see cref="DailyPortfolioValue.BrokerageInvestedUsd"/>, so a reader can tell "no split measured" from "no snapshot".
    /// </summary>
    Task<IReadOnlyList<DailyPortfolioValue>> GetAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>
/// One snapshot day's invested brokerage value, its cash-inclusive <see cref="BrokerageTotalUsd"/> (the scope the
/// book's TWR is measured on: buying from idle cash moves value inside it, so it is not a gain) and the day's
/// quality <see cref="Signals"/> (none when the source records none). <see cref="BrokerageInvestedUsd"/> is null on
/// a day with no cash/invested split - never zero.
/// </summary>
public sealed record DailyPortfolioValue(
    DateOnly Date, decimal? BrokerageInvestedUsd, HeadlineSignals? Signals = null, decimal? BrokerageTotalUsd = null);
