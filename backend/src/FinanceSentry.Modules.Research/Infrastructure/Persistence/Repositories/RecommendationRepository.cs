namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Reads run under the Owner filter. The rule sweep, the expiry job, the outcome job and the scoreboard (which the weekly
// brief reads) run with no person in scope, so their reads are the …Unscoped methods, which opt out explicitly and keep their own UserId predicate.
public sealed class RecommendationRepository(ResearchDbContext db) : IRecommendationRepository
{
    public Task<Recommendation?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
        => db.Recommendations.FirstOrDefaultAsync(r => r.UserId == userId && r.Id == id, ct);

    // The request-path reads leave out a pending row past its expiry; the daily expiry job journals it.
    public Task<Recommendation?> FindPendingAsync(Guid userId, string ticker, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return db.Recommendations.AsNoTracking().FirstOrDefaultAsync(
            r => r.UserId == userId && r.Ticker == ticker && r.Status == RecommendationStatus.Pending && r.ExpiresAt > now,
            ct);
    }

    public async Task<IReadOnlyList<Recommendation>> ListPendingAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.Recommendations.AsNoTracking()
            .Where(r => r.UserId == userId && r.Status == RecommendationStatus.Pending && r.ExpiresAt > now)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Recommendation>> ListClosedSinceAsync(
        Guid userId, DateTimeOffset since, CancellationToken ct = default)
        => await db.Recommendations.AsNoTracking()
            .Where(r => r.UserId == userId && r.Status != RecommendationStatus.Pending
                && (r.DecidedAt ?? r.ExpiresAt) >= since)
            .OrderByDescending(r => r.DecidedAt ?? r.ExpiresAt)
            .ToListAsync(ct);

    public Task<Recommendation?> FindPendingUnscopedAsync(Guid userId, string ticker, CancellationToken ct = default)
        => db.Recommendations
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .FirstOrDefaultAsync(
                r => r.UserId == userId && r.Ticker == ticker && r.Status == RecommendationStatus.Pending, ct);

    public Task<bool> ExistsForTriggerSinceUnscopedAsync(
        Guid userId, string ticker, RecommendationTriggerKind kind, string triggerRef, DateTimeOffset since,
        CancellationToken ct = default)
        => db.Recommendations
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AnyAsync(r => r.UserId == userId && r.Ticker == ticker && r.TriggerKind == kind
                && r.TriggerRef == triggerRef && r.CreatedAt >= since, ct);

    public async Task<IReadOnlyList<Recommendation>> ListExpiredUnscopedAsync(
        DateTimeOffset asOf, CancellationToken ct = default)
        => await db.Recommendations
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(r => r.Status == RecommendationStatus.Pending && r.ExpiresAt <= asOf)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Recommendation>> ListOutcomeOpenUnscopedAsync(CancellationToken ct = default)
        => await db.Recommendations
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(r => (r.Status == RecommendationStatus.Confirmed || r.Status == RecommendationStatus.Rejected)
                && r.DecidedAt != null && r.OutcomeScoredAt == null)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Recommendation>> ListClosedUnscopedAsync(Guid userId, CancellationToken ct = default)
        => await db.Recommendations.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(r => r.UserId == userId && r.Status != RecommendationStatus.Pending)
            .ToListAsync(ct);

    // Race-safe over the unique partial index on (UserId, Ticker) WHERE Status = 'Pending': the loser's SaveChanges
    // fails on the index and surfaces as a conflict rather than a second pending proposal. A superseded row is saved
    // in the same transaction, so the ticker never ends up with no pending proposal.
    public async Task AddPendingAsync(
        Recommendation recommendation, Recommendation? supersedes = null, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (supersedes is not null)
        {
            db.Recommendations.Update(supersedes);
            await db.SaveChangesAsync(ct);
        }

        db.Recommendations.Add(recommendation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Drop the rejected insert so the shared context does not retry it on the next SaveChanges.
            db.Entry(recommendation).State = EntityState.Detached;
            throw new RecommendationConflictException(recommendation.Ticker);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task UpdateAsync(Recommendation recommendation, CancellationToken ct = default)
    {
        db.Recommendations.Update(recommendation);
        await db.SaveChangesAsync(ct);
    }
}
