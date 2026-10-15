namespace FinanceSentry.Modules.Wealth.Domain.Ports;

using FinanceSentry.Core.Headlines;

/// <summary>
/// Published read port (#673): a user's daily brokerage-sleeve totals and invested values from the net-worth snapshot
/// table, for cross-module readers such as Radar's book-vs-benchmark change in invested value. Implemented inside
/// Wealth; the FinanceSentry.Integration adapter reaches Wealth only through this interface.
/// </summary>
public interface IBrokerageValueHistoryReader
{
    /// <summary>
    /// One row per snapshot dated within [<paramref name="from"/>, <paramref name="to"/>], ordered
    /// oldest to newest; empty when no snapshots exist for the range.
    /// </summary>
    Task<IReadOnlyList<DailyBrokerageValue>> GetDailyAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>
/// One day's brokerage sleeve in USD: the total (idle broker cash included) and the invested positions alone.
/// <see cref="BrokerageInvestedUsd"/> is null on a day with no cash/invested split - never zero, never the total.
/// <see cref="Signals"/> are the day's quality flags as they bear on the brokerage sleeve: the sleeve carried
/// forward from an earlier day, a reconstructed row, and the snapshot's recorded conversion status (which covers
/// every sleeve of that day, so it can over-report for brokerage, never under-report).
/// </summary>
public sealed record DailyBrokerageValue(
    DateOnly Date, decimal BrokerageTotalUsd, decimal? BrokerageInvestedUsd, HeadlineSignals? Signals = null);
