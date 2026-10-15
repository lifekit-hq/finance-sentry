namespace FinanceSentry.Core.Interfaces;

using FinanceSentry.Core.Utils;

public interface INetWorthSnapshotService
{
    Task PersistSnapshotAsync(
        Guid userId,
        NetWorthSnapshotData data,
        CancellationToken ct = default);
}

/// <param name="BrokerageInvested">
/// Invested (non-cash) part of <paramref name="BrokerageTotal"/> as measured this run, from
/// <see cref="IBookFiguresService"/>; null when the book figures could not be trusted (a stale source).
/// </param>
/// <param name="CryptoInvested">Invested part of <paramref name="CryptoTotal"/>; null as <paramref name="BrokerageInvested"/>.</param>
/// <param name="FxStatus">
/// The worst conversion status among the figures the totals were summed from; stored on the snapshot row so a
/// history read can name a seed-table or 1:1 conversion.
/// </param>
/// <param name="Sources">
/// What each registered source read this run (<see cref="BookFigures.Sources"/>), written as that snapshot's
/// per-source rows. Null = the caller has
/// no per-source figures and the day's existing per-source rows are left as they are; an empty list replaces
/// them with none.
/// </param>
public record NetWorthSnapshotData(
    DateOnly SnapshotDate,
    decimal BankingTotal,
    decimal BrokerageTotal,
    decimal CryptoTotal,
    bool BankingFresh = true,
    bool BrokerageFresh = true,
    bool CryptoFresh = true,
    string Currency = "USD",
    decimal? BrokerageInvested = null,
    decimal? CryptoInvested = null,
    FxStatus FxStatus = FxStatus.Exact,
    IReadOnlyList<BookSourceReading>? Sources = null);
