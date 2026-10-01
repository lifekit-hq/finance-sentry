namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary><see cref="IRiskToleranceReader"/> impl over Research's own <see cref="GetIpsQuery"/>.</summary>
public sealed class RiskToleranceReader(IQueryHandler<GetIpsQuery, IpsDto?> getIps) : IRiskToleranceReader
{
    public async Task<IpsRiskTolerance?> GetCurrentAsync(Guid userId, CancellationToken ct = default)
    {
        var ips = await getIps.Handle(new GetIpsQuery(userId), ct);
        return ips is null ? null : new IpsRiskTolerance(ips.MaxDrawdownTolerancePct);
    }
}
