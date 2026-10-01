namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain.Repositories;

public record GetIpsQuery(Guid UserId) : IQuery<IpsDto?>;

public class GetIpsQueryHandler(IIpsRepository repo) : IQueryHandler<GetIpsQuery, IpsDto?>
{
    public async Task<IpsDto?> Handle(GetIpsQuery query, CancellationToken ct)
    {
        var ips = await repo.GetCurrentUnscopedAsync(query.UserId, ct);
        if (ips is null)
        {
            return null;
        }

        return new IpsDto(
            ips.Id, ips.Version, ips.IsCurrent, ips.Goals, ips.PrimaryHorizonYears,
            ips.EmergencyCushionUsd, ips.RiskTolerance, ips.RiskCapacity, ips.MaxDrawdownTolerancePct, ips.RiskMeasuredAt,
            ips.AllocationTargets, ips.RebalancingRule, ips.ContributionPlan, ips.SellDiscipline,
            ips.CoolingOffDays, ips.Exclusions, ips.ReviewCadence,
            ips.LastReviewedAt, ips.CreatedAt, ips.UpdatedAt);
    }
}
