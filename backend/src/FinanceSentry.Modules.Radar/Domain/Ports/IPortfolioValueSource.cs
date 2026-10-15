namespace FinanceSentry.Modules.Radar.Domain.Ports;

using FinanceSentry.Core.Headlines;

/// <summary>
/// Read-port: daily invested brokerage value for a user (idle broker cash excluded), sourced from net-worth snapshots.
/// Lives in Radar's domain so the BookPerformanceService can consume portfolio history without
/// a compile-time dependency on the Wealth module.
/// </summary>
public interface IPortfolioValueSource
{
    /// <summary>
    /// Daily invested brokerage values for the user from <paramref name="from"/> through <paramref name="to"/>,
    /// ordered oldest→newest. Days with no cash/invested split are left out. Returns an empty list when no
    /// day in the range has one.
    /// </summary>
    Task<IReadOnlyList<DailyPortfolioValue>> GetAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>One day's invested brokerage value and the day's quality <see cref="Signals"/> (none when the source records none).</summary>
public sealed record DailyPortfolioValue(DateOnly Date, decimal BrokerageInvestedUsd, HeadlineSignals? Signals = null);
