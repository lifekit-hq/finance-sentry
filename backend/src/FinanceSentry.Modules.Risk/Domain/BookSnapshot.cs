namespace FinanceSentry.Modules.Risk.Domain;

using FinanceSentry.Core.Domain;

/// <summary>In-memory aggregate of the live book, built fresh per check run. Never persisted directly.</summary>
public sealed record BookSnapshot(
    decimal TotalUsd,
    decimal CashUsd,
    IReadOnlyList<BookPosition> Positions,
    bool IsStale,
    IReadOnlyList<string> StaleSources,
    decimal InvestedUsd)
{
    public static BookSnapshot Empty { get; } = new(0m, 0m, [], false, [], 0m);
}

/// <summary>
/// <paramref name="Sleeve"/> is the coarse Crypto/Brokerage split used by the concentration rules
/// (MaxPositionWeight/MaxSleeveWeight). <paramref name="AssetClass"/> is the finer
/// <see cref="AssetClassNormalizer"/> bucket (Equities/Bonds/Crypto/Cash/...) the allocation-drift
/// rule matches against the IPS — the two taxonomies are not interchangeable (finance-sentry#690:
/// matching drift targets against <see cref="Sleeve"/> silently reported 0% for every non-crypto
/// asset class, since an IPS target like "Equities" never equals "brokerage").
/// </summary>
public sealed record BookPosition(
    string Symbol,
    string Sleeve,
    decimal Quantity,
    decimal UsdValue,
    decimal WeightPct,
    string AssetClass = AssetClassNormalizer.Other);
