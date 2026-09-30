namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain.PolicyReviews;
using FinanceSentry.Modules.Research.Domain.Repositories;

public record GetPolicyReviewStatusQuery(Guid UserId) : IQuery<PolicyReviewStatusDto>;

public class GetPolicyReviewStatusQueryHandler(
    IIpsRepository ipsRepo,
    IPolicyReviewRepository reviewRepo,
    TimeProvider timeProvider)
    : IQueryHandler<GetPolicyReviewStatusQuery, PolicyReviewStatusDto>
{
    public async Task<PolicyReviewStatusDto> Handle(GetPolicyReviewStatusQuery query, CancellationToken ct)
    {
        var versions = await ipsRepo.ListVersionsAsync(query.UserId, ct);
        var latest = await reviewRepo.GetLatestAsync(query.UserId, ct);
        var latestDto = latest is null ? null : ToDto(latest);

        if (!versions.Any(v => v.IsCurrent))
            return new PolicyReviewStatusDto(false, null, false, null, null, false, false, 0, latestDto);

        var schedule = PolicyReviewSchedule.Evaluate(versions, timeProvider.GetUtcNow());
        return new PolicyReviewStatusDto(
            true,
            schedule.Cadence,
            schedule.CadenceRecognised,
            schedule.LastReviewedAt,
            schedule.DueAt,
            schedule.IsDue,
            schedule.IsMissed,
            schedule.DaysOverdue,
            latestDto);
    }

    private static PolicyReviewDto ToDto(PolicyReview r) => new(
        r.Id,
        r.PolicyStatementVersion,
        r.ReviewCadence,
        r.DueAt,
        r.CompletedAt,
        r.DaysOverdue,
        r.WasMissed,
        r.TotalValueUsd,
        r.Sleeves,
        r.Adjustments,
        r.Rationale);
}
