namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class MaterialityTermRepository(ResearchDbContext db) : IMaterialityTermRepository
{
    public async Task<IReadOnlyList<string>> ListEnabledTermsAsync(CancellationToken ct = default)
        => await db.MaterialityTerms.AsNoTracking()
            .Where(t => t.Enabled)
            .Select(t => t.Term)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MaterialityTerm>> ListAllAsync(CancellationToken ct = default)
        => await db.MaterialityTerms.AsNoTracking().OrderBy(t => t.Term).ToListAsync(ct);

    public async Task<Guid> AddAsync(MaterialityTerm term, CancellationToken ct = default)
    {
        db.MaterialityTerms.Add(term);
        await db.SaveChangesAsync(ct);
        return term.Id;
    }

    public async Task UpdateAsync(MaterialityTerm term, CancellationToken ct = default)
    {
        db.MaterialityTerms.Update(term);
        await db.SaveChangesAsync(ct);
    }
}
