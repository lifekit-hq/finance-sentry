namespace FinanceSentry.Modules.Wealth.Domain.Ports;

/// <summary>
/// Published read port: the invested book's (brokerage and crypto sources) cash against its total per snapshot day,
/// from the per-source rows of the net-worth snapshots, for cross-module readers that chart allocation over time.
/// Implemented inside Wealth; the FinanceSentry.Integration adapter reaches Wealth only through this interface.
/// </summary>
public interface IBookCashHistoryReader
{
    /// <summary>
    /// One row per snapshot day in [<paramref name="from"/>, <paramref name="to"/>], oldest first. A day where a
    /// book source has no cash/invested split is left out: it is never counted as zero cash.
    /// </summary>
    Task<IReadOnlyList<DailyBookCash>> GetDailyAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>One day's cash and total across the brokerage and crypto sources, in the snapshot's one currency.</summary>
public sealed record DailyBookCash(DateOnly Date, decimal CashUsd, decimal TotalUsd);
