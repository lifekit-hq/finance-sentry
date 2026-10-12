namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class AnalystUniverseRepository(ResearchDbContext db) : IAnalystUniverseRepository
{
    public async Task<IReadOnlyList<AnalystUniverseMember>> ListActiveAsync(CancellationToken ct = default)
        => await db.AnalystUniverseMembers.AsNoTracking().Where(m => m.Active).ToListAsync(ct);

    public async Task<IReadOnlyList<AnalystUniverseMember>> ListAllAsync(CancellationToken ct = default)
        => await db.AnalystUniverseMembers.AsNoTracking().ToListAsync(ct);

    // One INSERT ... ON CONFLICT over the unique Ticker index: the write is atomic, so two overlapping refreshes
    // update the same rows instead of the loser failing on the index, and no read precedes it. It runs at once
    // rather than at SaveChanges, and a member already tracked by this context is not refreshed. A re-listed member
    // is reactivated and takes the new reason.
    public async Task UpsertMembersAsync(
        IReadOnlyCollection<AnalystUniverseMember> members, CancellationToken ct = default)
    {
        if (members.Count == 0)
        {
            return;
        }

        // ON CONFLICT cannot touch one row twice in a statement, so a repeated ticker keeps its last member.
        var incoming = members
            .GroupBy(m => m.Ticker)
            .Select(g => g.Last())
            .ToList();

        var ids = incoming.Select(m => m.Id).ToArray();
        var tickers = incoming.Select(m => m.Ticker).ToArray();
        var reasons = incoming.Select(m => m.Reason.ToString()).ToArray();
        var addedAts = incoming.Select(m => m.AddedAt).ToArray();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO research.analyst_universe_members ("Id", "Ticker", "Reason", "Active", "AddedAt")
            SELECT id, ticker, reason, true, added_at FROM unnest(
                {ids}::uuid[], {tickers}::varchar[], {reasons}::varchar[], {addedAts}::timestamptz[])
                AS u(id, ticker, reason, added_at)
            ON CONFLICT ("Ticker") DO UPDATE SET
                "Active" = true,
                "Reason" = EXCLUDED."Reason"
            """,
            ct);
    }

    public async Task DeactivateAsync(IReadOnlyCollection<string> tickers, CancellationToken ct = default)
    {
        if (tickers.Count == 0)
        {
            return;
        }

        await db.AnalystUniverseMembers
            .Where(m => tickers.Contains(m.Ticker) && m.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Active, false), ct);
    }

    public async Task<bool> IsInUniverseAsync(string ticker, CancellationToken ct = default)
    {
        var upper = ticker.Trim().ToUpperInvariant();
        return await db.AnalystUniverseMembers.AsNoTracking()
            .AnyAsync(m => m.Ticker == upper && m.Active, ct);
    }
}
