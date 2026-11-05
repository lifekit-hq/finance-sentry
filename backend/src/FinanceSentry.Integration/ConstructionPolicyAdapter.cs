namespace FinanceSentry.Integration;

using FinanceSentry.Modules.PortfolioConstruction.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Ports;

/// <summary>
/// Spec 726 FR-014/FR-015: implements the Portfolio Construction module's <see cref="IConstructionPolicySource"/> by
/// reading the viewer's IPS through Research's published <see cref="IIpsAllocationReader"/> (sleeve targets) and
/// <see cref="IIpsExclusionsReader"/> ports and their risk limits through Risk's published
/// <see cref="IRiskLimitsReader"/> port (#673). All three are unchanged logic. The risk limits are fractions in
/// (0,1]; this adapter turns them into whole percentage points. Lives in the host so no module references another.
/// </summary>
public sealed class ConstructionPolicyAdapter(
    IIpsAllocationReader ipsAllocation,
    IIpsExclusionsReader ipsExclusions,
    IRiskLimitsReader riskLimits) : IConstructionPolicySource
{
    private const decimal FractionToPercent = 100m;

    public async Task<ConstructionPolicy> GetAsync(Guid userId, CancellationToken ct)
    {
        var allocation = await ipsAllocation.GetCurrentAsync(userId, ct);
        var exclusions = await ipsExclusions.GetCurrentAsync(userId, ct);
        var limits = await riskLimits.GetCurrentAsync(userId, ct);

        var sleeves = allocation is { Sleeves.Count: > 0 }
            ? allocation.Sleeves.Select(t => new PolicySleeve(t.AssetClass, t.TargetPct)).ToList()
            : null;

        return new ConstructionPolicy(
            exclusions,
            sleeves,
            limits?.MaxPositionWeightPct * FractionToPercent,
            limits?.MinCashBufferPct * FractionToPercent);
    }
}
