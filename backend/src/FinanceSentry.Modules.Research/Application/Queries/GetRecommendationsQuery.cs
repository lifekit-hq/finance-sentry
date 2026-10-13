namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>The Decisions queue (#1069): pending proposals oldest first, and those closed in the last 30 days.</summary>
public sealed record GetRecommendationsQuery(Guid UserId) : IQuery<RecommendationsResult>;

public sealed class GetRecommendationsQueryHandler(IRecommendationRepository repo)
    : IQueryHandler<GetRecommendationsQuery, RecommendationsResult>
{
    public const int DecidedWindowDays = 30;

    public async Task<RecommendationsResult> Handle(GetRecommendationsQuery query, CancellationToken ct)
    {
        var pending = await repo.ListPendingAsync(query.UserId, ct);
        var decided = await repo.ListClosedSinceAsync(query.UserId, DateTimeOffset.UtcNow.AddDays(-DecidedWindowDays), ct);
        return new RecommendationsResult(
            pending.Select(RecommendationDto.From).ToList(),
            decided.Select(RecommendationDto.From).ToList());
    }
}
