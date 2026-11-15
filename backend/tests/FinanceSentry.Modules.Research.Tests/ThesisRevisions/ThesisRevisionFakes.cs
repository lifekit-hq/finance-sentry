namespace FinanceSentry.Modules.Research.Tests.ThesisRevisions;

using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Revisions;

/// <summary>An in-memory revision store that keeps the one-open-per-subject rule the partial unique index enforces.</summary>
internal sealed class InMemoryThesisRevisionRepository : IThesisRevisionRepository
{
    public List<ThesisRevision> Rows { get; } = [];

    public Task<ThesisRevision?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
        => Task.FromResult(Rows.FirstOrDefault(r => r.UserId == userId && r.Id == id));

    public Task<ThesisRevision?> FindPendingAsync(Guid userId, Guid subjectId, CancellationToken ct = default)
        => Task.FromResult(Rows.FirstOrDefault(r => r.UserId == userId && r.SubjectId == subjectId
            && r.Status == ThesisRevisionStatus.Pending));

    public Task<IReadOnlyList<ThesisRevision>> ListPendingAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ThesisRevision>>(Rows
            .Where(r => r.UserId == userId && r.Status == ThesisRevisionStatus.Pending && r.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderBy(r => r.CreatedAt).ToList());

    public Task<IReadOnlyList<ThesisRevision>> ListClosedSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ThesisRevision>>(Rows
            .Where(r => r.UserId == userId && r.Status != ThesisRevisionStatus.Pending && (r.DecidedAt ?? r.ExpiresAt) >= since)
            .ToList());

    public Task<IReadOnlyList<ThesisRevision>> ListExpiredUnscopedAsync(DateTimeOffset asOf, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ThesisRevision>>(Rows
            .Where(r => r.Status == ThesisRevisionStatus.Pending && r.ExpiresAt <= asOf).ToList());

    public Task AddPendingAsync(ThesisRevision revision, ThesisRevision? supersedes = null, CancellationToken ct = default)
    {
        if (Rows.Any(r => r.UserId == revision.UserId && r.SubjectId == revision.SubjectId
            && r.Status == ThesisRevisionStatus.Pending && !ReferenceEquals(r, supersedes)))
        {
            throw new ThesisRevisionConflictException(revision.Ticker);
        }

        Rows.Add(revision);
        return Task.CompletedTask;
    }

    public Task<bool> TryClaimAsync(
        Guid userId, Guid id, ThesisRevisionStatus status, string? note, DateTimeOffset decidedAt, CancellationToken ct = default)
    {
        var row = Rows.FirstOrDefault(r => r.UserId == userId && r.Id == id && r.Status == ThesisRevisionStatus.Pending && r.ExpiresAt > decidedAt);
        if (row is null)
        {
            return Task.FromResult(false);
        }

        row.Status = status;
        row.DecidedAt = decidedAt;
        row.DecisionNote = note;
        return Task.FromResult(true);
    }

    public Task<ThesisRevisionStatus> ReleaseClaimAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var row = Rows.FirstOrDefault(r => r.UserId == userId && r.Id == id && r.Status == ThesisRevisionStatus.Confirmed);
        if (row is null)
        {
            return Task.FromResult(ThesisRevisionStatus.Pending);
        }

        var newer = Rows.FirstOrDefault(r => r.UserId == userId && r.SubjectId == row.SubjectId && r.Status == ThesisRevisionStatus.Pending);
        row.DecisionNote = null;
        if (newer is not null)
        {
            row.Status = ThesisRevisionStatus.Superseded;
            row.SupersededById = newer.Id;
            return Task.FromResult(ThesisRevisionStatus.Superseded);
        }

        row.Status = ThesisRevisionStatus.Pending;
        row.DecidedAt = null;
        return Task.FromResult(ThesisRevisionStatus.Pending);
    }

    public Task UpdateAsync(ThesisRevision revision, CancellationToken ct = default) => Task.CompletedTask;
}
