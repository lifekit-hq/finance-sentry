namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;

/// <summary><see cref="IRiskProfileReader"/> impl over Research's own <see cref="GetIpsQuery"/> and <see cref="GetPolicyReviewStatusQuery"/>.</summary>
public sealed class RiskProfileReader(
    IQueryHandler<GetIpsQuery, IpsDto?> getIps,
    IQueryHandler<GetPolicyReviewStatusQuery, PolicyReviewStatusDto> getReviewStatus) : IRiskProfileReader
{
    public async Task<IpsRiskProfile?> GetCurrentAsync(Guid userId, CancellationToken ct = default)
    {
        var ips = await getIps.Handle(new GetIpsQuery(userId), ct);
        if (ips is null)
            return null;

        var review = await getReviewStatus.Handle(new GetPolicyReviewStatusQuery(userId), ct);
        return new IpsRiskProfile(
            ips.RiskTolerance,
            ips.RiskCapacity,
            ips.MaxDrawdownTolerancePct,
            ips.RiskMeasuredAt,
            review.RiskRemeasurementDue,
            ips.PrimaryHorizonYears,
            ips.EmergencyCushionUsd);
    }
}
