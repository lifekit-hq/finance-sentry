namespace FinanceSentry.Core.Interfaces;

using FinanceSentry.Core.Utils;

public interface INetWorthSnapshotService
{
    Task PersistSnapshotAsync(
        Guid userId,
        NetWorthSnapshotData data,
        CancellationToken ct = default);
}

/// <summary>
/// One registered <see cref="IWealthSource"/>'s read for a snapshot run. <see cref="Reading"/> is null when the
/// read threw: the source then contributed nothing, as it does in <see cref="BookFigures"/>, and its row says so.
/// </summary>
public sealed record SourceSnapshotFigure(string SourceId, EconomyKind Kind, WealthSourceReading? Reading);

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
/// What each registered source read this run, written as that snapshot's per-source rows. Null = the caller has
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
    IReadOnlyList<SourceSnapshotFigure>? Sources = null);
