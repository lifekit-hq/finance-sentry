namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Core.Headlines;

/// <summary>
/// <see cref="TotalHeadline"/> is <see cref="TotalValueUsd"/> as a typed <see cref="Headline"/> (USD): degraded
/// with a STALE_SOURCE reason per source that failed to read (the same sources <see cref="IsStale"/> folds into one
/// flag), and the FX reason of the worst conversion behind it.
/// </summary>
public record AllocationDriftDto(
    bool HasIps,
    decimal TotalValueUsd,
    decimal CashUsd,
    decimal InvestedValueUsd,
    bool NeedsRebalance,
    IReadOnlyList<AllocationSleeveDrift> Sleeves,
    string RebalancingCadence,
    bool IsStale = false,
    Headline? TotalHeadline = null);

/// <summary>
/// One asset-class sleeve: its IPS target and effective bands vs. the actual current weight.
/// Status is Within / OverBand (trim) / UnderBand (add) / Unplanned (held but not in policy).
/// </summary>
public record AllocationSleeveDrift(
    string AssetClass,
    decimal TargetPct,
    decimal MinPct,
    decimal MaxPct,
    decimal ActualPct,
    decimal ActualValueUsd,
    decimal DriftPct,
    string Status);
