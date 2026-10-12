namespace FinanceSentry.Core.Interfaces;

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
    decimal? CryptoInvested = null);
