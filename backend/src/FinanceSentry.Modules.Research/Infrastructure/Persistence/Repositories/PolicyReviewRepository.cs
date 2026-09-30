namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Research.Domain.PolicyReviews;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class PolicyReviewRepository(ResearchDbContext db) : IPolicyReviewRepository
{
    public async Task RecordAsync(PolicyReview review, CancellationToken ct = default)
    {
        var statement = await db.PolicyStatements
            .FirstOrDefaultAsync(x => x.Id == review.PolicyStatementId && x.UserId == review.UserId, ct)
            ?? throw new InvalidOperationException(
                $"Policy statement {review.PolicyStatementId} no longer exists; the review cannot be recorded against it.");

        // The review and the statement's last-reviewed stamp land together or not at all, so a
        // recorded review always advances the schedule and a failed one leaves it due.
        statement.LastReviewedAt = review.CompletedAt;
        db.PolicyReviews.Add(review);
        await db.SaveChangesAsync(ct);
    }

    public Task<PolicyReview?> GetLatestAsync(Guid userId, CancellationToken ct = default)
        => db.PolicyReviews.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CompletedAt)
            .FirstOrDefaultAsync(ct);
}
