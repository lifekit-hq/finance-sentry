namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// Reads run under the Owner filter. The snapshot job's materializer runs with no person in scope, so its reads opt
// out explicitly (ListPreviousRunUnscopedAsync and the replace's earlier-rows lookup) and keep their own UserId
// predicate; a filtered lookup would find no earlier rows and duplicate the run.
public class BenchmarkRelativeRecordRepository(ResearchDbContext db) : IBenchmarkRelativeRecordRepository
{
    public async Task ReplaceRunAsync(
        Guid userId, DateTimeOffset asOf, IReadOnlyList<BenchmarkRelativeRecord> rows, CancellationToken ct = default)
    {
        // The delete is its own statement now, so a transaction keeps the replace atomic: a failed insert
        // must not leave the run deleted.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.BenchmarkRelativeRecords
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(r => r.UserId == userId && r.AsOf == asOf)
            .ExecuteDeleteAsync(ct);

        db.BenchmarkRelativeRecords.AddRange(rows);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<BenchmarkRelativeRecord>> ListPreviousRunUnscopedAsync(
        Guid userId, DateTimeOffset asOf, CancellationToken ct = default)
    {
        var records = db.BenchmarkRelativeRecords.AsNoTracking().IgnoreQueryFilters([OwnerQueryFilter.Name]);
        var previous = await records
            .Where(r => r.UserId == userId && r.AsOf < asOf)
            .OrderByDescending(r => r.AsOf)
            .Select(r => (DateTimeOffset?)r.AsOf)
            .FirstOrDefaultAsync(ct);

        return previous is null ? [] : await ListRunAsync(records, userId, previous.Value, ct);
    }

    public async Task<IReadOnlyList<BenchmarkRelativeRecord>> ListLatestRunAsync(
        Guid userId, CancellationToken ct = default)
    {
        var records = db.BenchmarkRelativeRecords.AsNoTracking();
        var latest = await records
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.AsOf)
            .Select(r => (DateTimeOffset?)r.AsOf)
            .FirstOrDefaultAsync(ct);

        return latest is null ? [] : await ListRunAsync(records, userId, latest.Value, ct);
    }

    private static async Task<IReadOnlyList<BenchmarkRelativeRecord>> ListRunAsync(
        IQueryable<BenchmarkRelativeRecord> records, Guid userId, DateTimeOffset asOf, CancellationToken ct)
        => await records
            .Where(r => r.UserId == userId && r.AsOf == asOf)
            .OrderBy(r => r.Scope)
            .ThenBy(r => r.Label)
            .ThenBy(r => r.Window)
            .ToListAsync(ct);
}
