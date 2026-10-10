namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// Reads run under the Owner filter. Jobs, cross-module readers and the handlers they share run with no person in
// scope, so they call the …Unscoped… methods, which opt out explicitly; the upsert existence check does the same
// (a filtered check would find nothing and re-insert). Each opted-out query keeps its own UserId predicate, or is
// an explicit all-users sweep.
public class ThesisRepository(ResearchDbContext db) : IThesisRepository
{
    public async Task<IReadOnlyList<InvestmentThesis>> ListAsync(Guid userId, CancellationToken ct = default)
        => await db.Theses.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<InvestmentThesis>> ListUnscopedAsync(Guid userId, CancellationToken ct = default)
        => await db.Theses.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetUserIdsWithThesesUnscopedAsync(CancellationToken ct = default)
        => await db.Theses.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Select(t => t.UserId)
            .Distinct()
            .ToListAsync(ct);

    public Task<InvestmentThesis?> FindAsync(Guid userId, Guid id, CancellationToken ct = default)
        => db.Theses.FirstOrDefaultAsync(t => t.UserId == userId && t.Id == id, ct);

    public async Task<IReadOnlyList<InvestmentThesis>> FindByTickerAsync(
        Guid userId, string ticker, CancellationToken ct = default)
        => await db.Theses.AsNoTracking()
            .Where(t => t.UserId == userId && t.Ticker == ticker)
            .ToListAsync(ct);

    public async Task UpsertAsync(InvestmentThesis thesis, CancellationToken ct = default)
    {
        var existing = await db.Theses.IgnoreQueryFilters([OwnerQueryFilter.Name]).FirstOrDefaultAsync(
            t => t.UserId == thesis.UserId && t.Id == thesis.Id, ct);

        if (existing is null)
        {
            db.Theses.Add(thesis);
        }
        else
        {
            existing.ThesisText = thesis.ThesisText;
            existing.Ticker = thesis.Ticker;
            existing.KeyDataPoints = thesis.KeyDataPoints;
            existing.Catalysts = thesis.Catalysts;
            existing.InvalidationTriggers = thesis.InvalidationTriggers;
            existing.EntryPrice = thesis.EntryPrice;
            existing.BrokenAt = thesis.BrokenAt;
            existing.BrokenReason = thesis.BrokenReason;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var affected = await db.Theses
            .Where(t => t.UserId == userId && t.Id == id)
            .ExecuteDeleteAsync(ct);
        return affected > 0;
    }
}
