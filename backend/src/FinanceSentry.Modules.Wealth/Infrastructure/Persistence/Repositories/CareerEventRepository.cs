namespace FinanceSentry.Modules.Wealth.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// The unsuffixed reads run under the Owner filter (the request's own person, or an MCP call acting as them). The
// projection readers a job reaches (the monthly brief) have no principal, so they use the Unscoped variants, which
// opt out by name and keep their own UserId predicate.
public class CareerEventRepository(WealthDbContext db) : ICareerEventRepository
{
    public bool CanReadEncryptedFields => db.HoldsEncryptionKey;

    public async Task<IReadOnlyList<CareerEvent>> ListAsync(Guid userId, CancellationToken ct = default)
        => await db.CareerEvents.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Date).ThenBy(c => c.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CareerEventOutline>> ListOutlinesAsync(Guid userId, CancellationToken ct = default)
        => await db.CareerEvents.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Date).ThenBy(c => c.CreatedAt)
            .Select(c => new CareerEventOutline(c.Id, c.Kind, c.Date, c.EndDate, c.Currency, c.RetiredAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CareerEvent>> ListUnscopedAsync(Guid userId, CancellationToken ct = default)
        => await db.CareerEvents.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Date).ThenBy(c => c.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CareerEventOutline>> ListOutlinesUnscopedAsync(Guid userId, CancellationToken ct = default)
        => await db.CareerEvents.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Date).ThenBy(c => c.CreatedAt)
            .Select(c => new CareerEventOutline(c.Id, c.Kind, c.Date, c.EndDate, c.Currency, c.RetiredAt))
            .ToListAsync(ct);

    public Task<CareerEvent?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
        => db.CareerEvents.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

    public async Task AddAsync(CareerEvent career, CancellationToken ct = default)
    {
        db.CareerEvents.Add(career);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(CareerEvent career, CancellationToken ct = default)
    {
        db.CareerEvents.Remove(career);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
