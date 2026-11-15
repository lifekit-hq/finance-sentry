namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Revisions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Reads run under the Owner filter. The expiry job runs with no person in scope, so its read is the …Unscoped method,
// which opts out explicitly and keeps its own predicate.
public sealed class ThesisRevisionRepository(ResearchDbContext db) : IThesisRevisionRepository
{
    public Task<ThesisRevision?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
        => db.ThesisRevisions.FirstOrDefaultAsync(r => r.UserId == userId && r.Id == id, ct);

    public Task<ThesisRevision?> FindPendingAsync(Guid userId, Guid subjectId, CancellationToken ct = default)
        => db.ThesisRevisions.FirstOrDefaultAsync(
            r => r.UserId == userId && r.SubjectId == subjectId && r.Status == ThesisRevisionStatus.Pending, ct);

    // The lists leave out a pending row past its expiry; the daily expiry job settles it.
    public async Task<IReadOnlyList<ThesisRevision>> ListPendingAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.ThesisRevisions.AsNoTracking()
            .Where(r => r.UserId == userId && r.Status == ThesisRevisionStatus.Pending && r.ExpiresAt > now)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ThesisRevision>> ListClosedSinceAsync(
        Guid userId, DateTimeOffset since, CancellationToken ct = default)
        => await db.ThesisRevisions.AsNoTracking()
            .Where(r => r.UserId == userId && r.Status != ThesisRevisionStatus.Pending
                && (r.DecidedAt ?? r.ExpiresAt) >= since)
            .OrderByDescending(r => r.DecidedAt ?? r.ExpiresAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ThesisRevision>> ListExpiredUnscopedAsync(
        DateTimeOffset asOf, CancellationToken ct = default)
        => await db.ThesisRevisions
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(r => r.Status == ThesisRevisionStatus.Pending && r.ExpiresAt <= asOf)
            .ToListAsync(ct);

    // Race-safe over the unique partial index on (UserId, SubjectId) WHERE Status = 'Pending': the loser's SaveChanges
    // fails on the index and surfaces as a conflict rather than a second pending proposal. A superseded row is saved
    // in the same transaction, so the subject never ends up with no pending proposal.
    public async Task AddPendingAsync(
        ThesisRevision revision, ThesisRevision? supersedes = null, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (supersedes is not null)
        {
            db.ThesisRevisions.Update(supersedes);
            await db.SaveChangesAsync(ct);
        }

        db.ThesisRevisions.Add(revision);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Drop the rejected insert so the shared context does not retry it on the next SaveChanges.
            db.Entry(revision).State = EntityState.Detached;
            throw new ThesisRevisionConflictException(revision.Ticker);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<bool> TryClaimAsync(
        Guid userId, Guid id, ThesisRevisionStatus status, string? note, DateTimeOffset decidedAt, CancellationToken ct = default)
        => await db.ThesisRevisions
            .Where(r => r.UserId == userId && r.Id == id && r.Status == ThesisRevisionStatus.Pending && r.ExpiresAt > decidedAt)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(r => r.Status, status)
                    .SetProperty(r => r.DecidedAt, decidedAt)
                    .SetProperty(r => r.DecisionNote, note),
                ct) == 1;

    public async Task ReleaseClaimAsync(Guid userId, Guid id, CancellationToken ct = default)
        => await db.ThesisRevisions
            .Where(r => r.UserId == userId && r.Id == id && r.Status == ThesisRevisionStatus.Confirmed)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(r => r.Status, ThesisRevisionStatus.Pending)
                    .SetProperty(r => r.DecidedAt, (DateTimeOffset?)null)
                    .SetProperty(r => r.DecisionNote, (string?)null),
                ct);

    public async Task UpdateAsync(ThesisRevision revision, CancellationToken ct = default)
    {
        db.ThesisRevisions.Update(revision);
        await db.SaveChangesAsync(ct);
    }
}
