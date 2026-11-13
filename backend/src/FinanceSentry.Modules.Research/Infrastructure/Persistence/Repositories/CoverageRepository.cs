namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public sealed class CoverageRepository(ResearchDbContext db) : ICoverageRepository
{
    public async Task<IReadOnlyList<CoverageMember>> ListMembersUnscopedAsync(Guid userId, CancellationToken ct = default)
        => await db.CoverageMembers.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Ticker)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListUserIdsUnscopedAsync(CancellationToken ct = default)
        => await db.CoverageMembers.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct);

    public async Task SaveEvaluationAsync(
        Guid userId,
        IReadOnlyCollection<CoverageMember> added,
        IReadOnlyCollection<CoverageMember> updated,
        IReadOnlyCollection<CoverageMember> removed,
        IReadOnlyCollection<CoverageJournalEntry> journal,
        CancellationToken ct = default)
    {
        if (added.Count + updated.Count + removed.Count + journal.Count == 0)
        {
            return;
        }

        // The context is shared by every user of a run: whatever this save leaves tracked, success or not, must not reach the next one.
        try
        {
            db.CoverageMembers.AddRange(added);
            db.CoverageMembers.UpdateRange(updated);

            // A removed row is deleted by its key: the member came from a no-tracking read.
            var removedIds = removed.Select(m => m.Id).ToList();
            if (removedIds.Count > 0)
            {
                var rows = await db.CoverageMembers
                    .IgnoreQueryFilters([OwnerQueryFilter.Name])
                    .Where(m => m.UserId == userId && removedIds.Contains(m.Id))
                    .ToListAsync(ct);
                db.CoverageMembers.RemoveRange(rows);
            }

            db.CoverageJournal.AddRange(journal);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<CoverageJournalEntry>> ListJournalUnscopedAsync(
        Guid userId, string? ticker, int take, CancellationToken ct = default)
    {
        var query = db.CoverageJournal.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(e => e.UserId == userId);

        if (!string.IsNullOrWhiteSpace(ticker))
        {
            query = query.Where(e => e.Ticker == ticker);
        }

        return await query.OrderByDescending(e => e.At).Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> ListLastRemovalsUnscopedAsync(
        Guid userId, CancellationToken ct = default)
    {
        var removals = await db.CoverageJournal.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(e => e.UserId == userId && e.Action == CoverageAction.Removed)
            .GroupBy(e => e.Ticker)
            .Select(g => new { Ticker = g.Key, At = g.Max(e => e.At) })
            .ToListAsync(ct);
        return removals.ToDictionary(r => r.Ticker, r => r.At);
    }

    public async Task<IReadOnlyList<CoverageVerdictObservation>> ListObservationsUnscopedAsync(
        Guid userId, DateOnly since, CancellationToken ct = default)
        => await db.CoverageVerdictObservations.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(o => o.UserId == userId && o.ObservedOn >= since)
            .OrderBy(o => o.ObservedOn)
            .ToListAsync(ct);

    public async Task<bool> AddObservationUnscopedAsync(CoverageVerdictObservation observation, CancellationToken ct = default)
    {
        var exists = await db.CoverageVerdictObservations
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AnyAsync(o => o.UserId == observation.UserId && o.Ticker == observation.Ticker
                && o.ObservedOn == observation.ObservedOn, ct);
        if (exists)
        {
            return false;
        }

        try
        {
            db.CoverageVerdictObservations.Add(observation);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        return true;
    }

    public async Task<int> PruneObservationsUnscopedAsync(DateOnly before, CancellationToken ct = default)
    {
        var old = await db.CoverageVerdictObservations
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(o => o.ObservedOn < before)
            .ToListAsync(ct);
        try
        {
            db.CoverageVerdictObservations.RemoveRange(old);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        return old.Count;
    }
}
