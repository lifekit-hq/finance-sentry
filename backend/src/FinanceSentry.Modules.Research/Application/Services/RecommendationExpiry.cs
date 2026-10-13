namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>Expires one pending proposal and journals it, so no proposal leaves the queue unrecorded.</summary>
public static class RecommendationExpiry
{
    public static async Task ExpireAsync(
        IRecommendationRepository repo, IThesisEventRecorder recorder, Recommendation recommendation, CancellationToken ct)
    {
        recommendation.Status = RecommendationStatus.Expired;
        recommendation.Execution = RecommendationExecution.NotApplicable;
        await repo.UpdateAsync(recommendation, ct);
        await recorder.RecordAsync(
            recommendation.UserId, ThesisSubjectType.Recommendation, recommendation.Id, recommendation.Ticker,
            ThesisEventType.Expired, "Expired undecided", ct);
    }
}
