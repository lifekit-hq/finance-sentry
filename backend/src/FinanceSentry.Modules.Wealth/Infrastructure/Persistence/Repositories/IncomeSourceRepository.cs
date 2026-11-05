namespace FinanceSentry.Modules.Wealth.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// Every read runs under the Owner filter: a declared income is read for the request's own person (the API, or an MCP
// call acting as them). No job reads it, so there is no Unscoped variant.
public class IncomeSourceRepository(WealthDbContext db) : IIncomeSourceRepository
{
    public bool CanReadEncryptedFields => db.HoldsEncryptionKey;

    public async Task<IReadOnlyList<IncomeSource>> ListAsync(Guid userId, CancellationToken ct = default)
        => await db.IncomeSources.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<IncomeSourceOutline>> ListOutlinesAsync(Guid userId, CancellationToken ct = default)
        => await db.IncomeSources.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.CreatedAt)
            .Select(s => new IncomeSourceOutline(s.Id, s.Kind, s.Currency, s.Cadence, s.GrossOrNet, s.StartDate, s.EndDate))
            .ToListAsync(ct);

    public Task<IncomeSource?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
        => db.IncomeSources.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);

    public async Task AddAsync(IncomeSource source, CancellationToken ct = default)
    {
        db.IncomeSources.Add(source);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(IncomeSource source, CancellationToken ct = default)
    {
        db.IncomeSources.Remove(source);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
