namespace FinanceSentry.Modules.Wealth.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// Reads run under the Owner filter and skip removed entries (the soft-delete filter). The wealth source is also read by the snapshot job, with no person in scope, so
// it calls ListUnscopedAsync, which opts out explicitly and keeps its own UserId predicate.
public class ManualHoldingRepository(WealthDbContext db) : IManualHoldingRepository
{
    public async Task<IReadOnlyList<ManualHolding>> ListAsync(Guid userId, CancellationToken ct = default)
        => await db.ManualHoldings.AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ManualHolding>> ListUnscopedAsync(Guid userId, CancellationToken ct = default)
        => await db.ManualHoldings.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ManualHolding>> ListIncludingRemovedUnscopedAsync(Guid userId, CancellationToken ct = default)
        => await db.ManualHoldings.IgnoreQueryFilters([OwnerQueryFilter.Name, WealthDbContext.ManualHoldingActiveFilterName]).AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(ct);

    public Task<ManualHolding?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
        => db.ManualHoldings.FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId, ct);

    public async Task AddAsync(ManualHolding holding, CancellationToken ct = default)
    {
        db.ManualHoldings.Add(holding);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
