namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

public sealed class CoverageMemoRepository(ResearchDbContext db) : ICoverageMemoRepository
{
    public async Task<IReadOnlyList<CoverageJournalEntry>> ListEntrantsWithoutMemoUnscopedAsync(
        Guid userId, DateTimeOffset since, CancellationToken ct = default)
    {
        var members = db.CoverageMembers.IgnoreQueryFilters([OwnerQueryFilter.Name]).Where(m => m.UserId == userId);
        var memos = db.CoverageMemos.IgnoreQueryFilters([OwnerQueryFilter.Name]).Where(m => m.UserId == userId);

        var entrants = await db.CoverageJournal.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(e => e.UserId == userId && e.Action == CoverageAction.Added && e.At >= since
                && e.Reason != CoverageReason.Held && e.Reason != CoverageReason.Watchlist
                && members.Any(m => m.Ticker == e.Ticker)
                && !memos.Any(m => m.JournalEntryId == e.Id))
            .ToListAsync(ct);

        // Ordered in memory: a null rank (no rank was recorded) sorts last on every provider.
        return [.. entrants
            .OrderByDescending(e => e.RankScore ?? int.MinValue)
            .ThenBy(e => e.At)
            .ThenBy(e => e.Ticker, StringComparer.Ordinal)];
    }

    public async Task<(int Count, decimal CostUsd)> TotalsSinceUnscopedAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        var rows = await db.CoverageMemos.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(m => m.CreatedAt >= since)
            .Select(m => m.CostUsd)
            .ToListAsync(ct);
        return (rows.Count, rows.Sum());
    }

    public async Task<bool> AddUnscopedAsync(CoverageMemo memo, CancellationToken ct = default)
    {
        try
        {
            db.CoverageMemos.Add(memo);
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent run wrote the memo for this entry between the check and the insert.
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<CoverageMemo?> GetLatestUnscopedAsync(Guid userId, string ticker, CancellationToken ct = default)
        => (await db.CoverageMemos.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(m => m.UserId == userId && m.Ticker == ticker)
            .ToListAsync(ct))
            .MaxBy(m => m.CreatedAt);

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> LatestTimesUnscopedAsync(
        Guid userId, CancellationToken ct = default)
    {
        var rows = await db.CoverageMemos.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(m => m.UserId == userId)
            .GroupBy(m => m.Ticker)
            .Select(g => new { Ticker = g.Key, At = g.Max(m => m.CreatedAt) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Ticker, r => r.At);
    }
}
