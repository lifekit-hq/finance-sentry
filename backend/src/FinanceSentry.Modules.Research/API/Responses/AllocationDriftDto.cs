namespace FinanceSentry.Modules.Research.API.Responses;

public record AllocationDriftDto(
    bool HasIps,
    decimal TotalValueUsd,
    decimal CashUsd,
    decimal InvestedValueUsd,
    bool NeedsRebalance,
    IReadOnlyList<AllocationSleeveDrift> Sleeves,
    string RebalancingCadence,
    bool IsStale = false);

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
