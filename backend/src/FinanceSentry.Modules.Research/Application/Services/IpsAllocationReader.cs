namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary><see cref="IIpsAllocationReader"/> impl over Research's own <see cref="GetIpsQuery"/>.</summary>
public sealed class IpsAllocationReader(IQueryHandler<GetIpsQuery, IpsDto?> getIps) : IIpsAllocationReader
{
    public async Task<IpsAllocationPolicy?> GetCurrentAsync(Guid userId, CancellationToken ct = default)
    {
        var ips = await getIps.Handle(new GetIpsQuery(userId), ct);
        if (ips is null)
        {
            return null;
        }

        var sleeves = ips.AllocationTargets
            .Select(t => new IpsAllocationSleeve(t.AssetClass, t.TargetPct, t.MinPct, t.MaxPct))
            .ToList();
        return new IpsAllocationPolicy(sleeves, ips.RebalancingRule.AbsoluteBandPct, ips.RebalancingRule.RelativeBandPct);
    }
}
