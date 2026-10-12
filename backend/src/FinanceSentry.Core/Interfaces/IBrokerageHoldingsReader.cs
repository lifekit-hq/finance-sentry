namespace FinanceSentry.Core.Interfaces;

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
    decimal? NativeValue = null);
