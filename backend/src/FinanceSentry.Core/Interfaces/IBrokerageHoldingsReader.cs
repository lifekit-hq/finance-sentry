namespace FinanceSentry.Core.Interfaces;

using FinanceSentry.Core.Utils;

public interface IBrokerageHoldingsReader
{
    Task<IReadOnlyList<BrokerageHoldingSummary>> GetHoldingsAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// "Verified", "Unverified" or "Unknown" — mirrors <c>BrokerageCostBasisReconciler.BasisState</c> without
/// this Core interface depending on the BrokerageSync module. <see cref="CostBasisUsd"/> is null whenever
/// this is not "Verified" (fs-688): a reader must never act on gain/loss it cannot trust.
/// <see cref="NativeValue"/> is the position's value in <see cref="Currency"/> as the provider reported it (never a
/// conversion); null for USD-only rows. Aggregations still sum <see cref="UsdValue"/> only.
/// <see cref="FxStatus"/> is how the rate table in force when this row is read would convert
/// <see cref="Currency"/>: the stored <see cref="UsdValue"/> does not record the rate it was converted at, so a
/// currency that was unknown or on the seed table at sync time and has a live rate now reads as exact here.
/// </summary>
public sealed record BrokerageHoldingSummary(
    string Symbol,
    string InstrumentType,
    decimal Quantity,
    decimal UsdValue,
    DateTime SyncedAt,
    string Provider,
    decimal? CostBasisUsd = null,
    string BasisState = "Unknown",
    DateOnly? FlexAsOfDate = null,
    string Currency = "USD",
    decimal? NativeValue = null,
    FxStatus FxStatus = FxStatus.Exact);
