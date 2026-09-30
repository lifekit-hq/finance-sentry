namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class BenchmarkRelativeRecordRepository(ResearchDbContext db) : IBenchmarkRelativeRecordRepository
{
    public async Task ReplaceRunAsync(
        Guid userId, DateTimeOffset asOf, IReadOnlyList<BenchmarkRelativeRecord> rows, CancellationToken ct = default)
    {
        var existing = await db.BenchmarkRelativeRecords
            .Where(r => r.UserId == userId && r.AsOf == asOf)
            .ToListAsync(ct);

        db.BenchmarkRelativeRecords.RemoveRange(existing);
        db.BenchmarkRelativeRecords.AddRange(rows);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BenchmarkRelativeRecord>> ListPreviousRunAsync(
        Guid userId, DateTimeOffset asOf, CancellationToken ct = default)
    {
        var previous = await db.BenchmarkRelativeRecords.AsNoTracking()
            .Where(r => r.UserId == userId && r.AsOf < asOf)
            .OrderByDescending(r => r.AsOf)
            .Select(r => (DateTimeOffset?)r.AsOf)
            .FirstOrDefaultAsync(ct);

        return previous is null ? [] : await ListRunAsync(userId, previous.Value, ct);
    }

    public async Task<IReadOnlyList<BenchmarkRelativeRecord>> ListLatestRunAsync(
        Guid userId, CancellationToken ct = default)
    {
        var latest = await db.BenchmarkRelativeRecords.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.AsOf)
            .Select(r => (DateTimeOffset?)r.AsOf)
            .FirstOrDefaultAsync(ct);

        return latest is null ? [] : await ListRunAsync(userId, latest.Value, ct);
    }

    private async Task<IReadOnlyList<BenchmarkRelativeRecord>> ListRunAsync(
        Guid userId, DateTimeOffset asOf, CancellationToken ct)
        => await db.BenchmarkRelativeRecords.AsNoTracking()
            .Where(r => r.UserId == userId && r.AsOf == asOf)
            .OrderBy(r => r.Scope)
            .ThenBy(r => r.Label)
            .ThenBy(r => r.Window)
            .ToListAsync(ct);
}
