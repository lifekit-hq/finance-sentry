namespace FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public sealed class RadarUniverseRepository(RadarDbContext db) : IRadarUniverseRepository
{
    public async Task<IReadOnlyList<RadarUniverseMember>> ListActiveAsync(CancellationToken ct = default)
        => await db.UniverseMembers.AsNoTracking()
            .Where(m => m.Active)
            .OrderBy(m => m.Ticker)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RadarUniverseMember>> ListAllAsync(CancellationToken ct = default)
        => await db.UniverseMembers.AsNoTracking()
            .OrderBy(m => m.Ticker)
            .ToListAsync(ct);

    // One INSERT ... ON CONFLICT per member on the unique Ticker index: the write is atomic, so two overlapping
    // refreshes update the same row instead of the loser failing on the index, and no read precedes it. It runs at
    // once rather than at SaveChanges, and a member already tracked by this context is not refreshed. A re-listed
    // member is reactivated and takes the new kind and source.
    public async Task UpsertMembersAsync(
        IReadOnlyCollection<RadarUniverseMember> members, CancellationToken ct = default)
    {
        if (members.Count == 0)
        {
            return;
        }

        foreach (var m in members)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO radar.radar_universe_members ("Ticker", "Kind", "Source", "Active")
                VALUES ({m.Ticker}, {m.Kind.ToString()}, {m.Source.ToString()}, true)
                ON CONFLICT ("Ticker") DO UPDATE SET
                    "Active" = true,
                    "Kind" = EXCLUDED."Kind",
                    "Source" = EXCLUDED."Source"
                """,
                ct);
        }
    }

    public async Task DeactivateAsync(IReadOnlyCollection<string> tickers, CancellationToken ct = default)
    {
        if (tickers.Count == 0)
        {
            return;
        }

        await db.UniverseMembers
            .Where(m => tickers.Contains(m.Ticker) && m.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Active, false), ct);
    }
}
