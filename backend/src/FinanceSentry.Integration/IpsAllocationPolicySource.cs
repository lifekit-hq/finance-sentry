namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Ports;

/// <summary>
/// 039: implements the Risk module's <see cref="IAllocationPolicySource"/> by reading the IPS - the
/// single home of target allocation - through Research's published <see cref="IIpsAllocationReader"/>
/// port (#673). Translates the IPS whole-percent target plus min/max band (or the RebalancingRule
/// 5/25 default) into the fraction target + symmetric drift band the Risk drift comparator
/// consumes. Lives in the host so neither module references the other.
/// </summary>
public sealed class IpsAllocationPolicySource(IIpsAllocationReader ipsAllocation)
    : IAllocationPolicySource
{
    private const decimal PercentToFraction = 100m;
    private const decimal RelativeBandDivisor = 100m;
    private const decimal HalfDivisor = 2m;

    public async Task<IReadOnlyList<AllocationDriftTarget>> GetAllocationTargetsAsync(
        Guid userId, CancellationToken ct)
    {
        var ips = await ipsAllocation.GetCurrentAsync(userId, ct);
        if (ips is null || ips.Sleeves.Count == 0)
        {
            return [];
        }

        var result = new List<AllocationDriftTarget>(ips.Sleeves.Count);
        foreach (var target in ips.Sleeves)
        {
            var targetFraction = target.TargetPct / PercentToFraction;

            // Prefer the explicit per-sleeve band (min/max, e.g. from a migrated Risk band) — a set max
            // is authoritative even when the lower bound reaches 0 (target ≤ band). Otherwise derive the
            // band from the RebalancingRule as GetAllocationDriftQuery does, so both drift evaluators
            // stay consistent for IPS-native allocations that carry no explicit min/max.
            decimal bandFraction;
            if (target.MaxPct > 0m)
            {
                bandFraction = (target.MaxPct - target.MinPct) / HalfDivisor / PercentToFraction;
            }
            else
            {
                var bandPercent = Math.Max(
                    ips.AbsoluteBandPct, target.TargetPct * ips.RelativeBandPct / RelativeBandDivisor);
                bandFraction = bandPercent / PercentToFraction;
            }

            result.Add(new AllocationDriftTarget(target.AssetClass, targetFraction, bandFraction));
        }

        return result;
    }
}
