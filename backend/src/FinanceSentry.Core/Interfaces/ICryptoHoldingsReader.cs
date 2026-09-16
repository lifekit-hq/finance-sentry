namespace FinanceSentry.Core.Interfaces;

public interface ICryptoHoldingsReader
{
    Task<IReadOnlyList<CryptoHoldingSummary>> GetHoldingsAsync(Guid userId, CancellationToken ct = default);
}

/// <param name="UsdValue">Already USD — sums over holdings are USD sums.</param>
/// <param name="IsVenueFiat">
/// Fiat cash held on the venue (<paramref name="Asset"/> is its currency code, the quantities its
/// native amount). Venue cash: counted in the venue's value and in cash, never as a crypto
/// position and never as bank cash.
/// </param>
public sealed record CryptoHoldingSummary(
    string Asset,
    decimal FreeQuantity,
    decimal LockedQuantity,
    decimal UsdValue,
    DateTime SyncedAt,
    string Provider,
    decimal? CostBasisUsd = null,
    bool IsVenueFiat = false);
