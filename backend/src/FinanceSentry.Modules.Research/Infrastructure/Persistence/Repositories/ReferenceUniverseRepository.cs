namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public sealed class ReferenceUniverseRepository(ResearchDbContext db) : IReferenceUniverseRepository
{
    public Task<int> CountBySourceAsync(string source, CancellationToken ct = default)
        => db.ReferenceListMembers.CountAsync(m => m.Source == source, ct);

    public async Task<int> ReplaceSourceAsync(
        string source, IReadOnlyCollection<ReferenceListMember> desired, CancellationToken ct = default)
    {
        var wanted = desired
            .GroupBy(m => (m.ListKey, m.Ticker))
            .ToDictionary(g => g.Key, g => g.First());

        var existing = await db.ReferenceListMembers.Where(m => m.Source == source).ToListAsync(ct);
        var kept = new HashSet<(string, string)>();
        foreach (var row in existing)
        {
            var key = (row.ListKey, row.Ticker);
            if (wanted.TryGetValue(key, out var next))
            {
                row.Name = next.Name;
                kept.Add(key);
            }
            else
            {
                db.ReferenceListMembers.Remove(row);
            }
        }

        db.ReferenceListMembers.AddRange(wanted.Where(w => !kept.Contains(w.Key)).Select(w => w.Value));
        await db.SaveChangesAsync(ct);
        return wanted.Count;
    }

    public async Task AddRunAsync(ReferenceRefreshRun run, CancellationToken ct = default)
    {
        db.ReferenceRefreshRuns.Add(run);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ReferenceListCount>> ListListsAsync(CancellationToken ct = default)
    {
        var groups = await db.ReferenceListMembers.AsNoTracking()
            .GroupBy(m => new { m.ListKey, m.Source })
            .Select(g => new { g.Key.ListKey, g.Key.Source, Count = g.Count() })
            .ToListAsync(ct);
        return groups
            .OrderBy(g => g.ListKey, StringComparer.Ordinal)
            .Select(g => new ReferenceListCount(g.ListKey, g.Source, g.Count))
            .ToList();
    }

    public async Task<IReadOnlyList<ReferenceListMember>> ListMembersAsync(string listKey, CancellationToken ct = default)
        => await db.ReferenceListMembers.AsNoTracking()
            .Where(m => m.ListKey == listKey)
            .OrderBy(m => m.Ticker)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<string>> ListKeysForTickerAsync(string ticker, CancellationToken ct = default)
        => await db.ReferenceListMembers.AsNoTracking()
            .Where(m => m.Ticker == ticker)
            .Select(m => m.ListKey)
            .OrderBy(k => k)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ReferenceRefreshRun>> ListRecentRunsAsync(int take, CancellationToken ct = default)
        => await db.ReferenceRefreshRuns.AsNoTracking()
            .OrderByDescending(r => r.FinishedAt)
            .Take(take)
            .ToListAsync(ct);
}
