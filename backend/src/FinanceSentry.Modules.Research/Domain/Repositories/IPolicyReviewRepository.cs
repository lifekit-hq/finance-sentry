namespace FinanceSentry.Modules.Research.Domain.Repositories;

using FinanceSentry.Modules.Research.Domain.PolicyReviews;

public interface IPolicyReviewRepository
{
    /// <summary>
    /// Persists a completed review and stamps its completion time as the reviewed statement's
    /// last-reviewed timestamp, in one unit of work. The scheduled review job records with no person in scope, so
    /// the statement lookup opts out of the Owner query filter and matches on the review's UserId.
    /// </summary>
    Task RecordAsync(PolicyReview review, CancellationToken ct = default);

    /// <summary>The user's most recent completed review, or null when none has run.</summary>
    Task<PolicyReview?> GetLatestAsync(Guid userId, CancellationToken ct = default);
}
