namespace FinanceSentry.Modules.Wealth.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// Every read runs under the Owner filter: the profile is read for the request's own person (the API, or an MCP call
// acting as them). No job reads it, so there is no Unscoped variant.
public class TaxProfileRepository(WealthDbContext db) : ITaxProfileRepository
{
    public bool CanReadEncryptedFields => db.HoldsEncryptionKey;

    public Task<TaxProfile?> GetAsync(Guid userId, CancellationToken ct = default)
        => db.TaxProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);

    // Null tests on the encrypted columns run in SQL; the ciphertext is never read back.
    public Task<TaxProfileOutline?> GetOutlineAsync(Guid userId, CancellationToken ct = default)
        => db.TaxProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new TaxProfileOutline(p.Jurisdiction, p.JurisdictionNotes != null, p.Caveat != null, p.UpdatedAt))
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(TaxProfile profile, CancellationToken ct = default)
    {
        db.TaxProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(TaxProfile profile, CancellationToken ct = default)
    {
        db.TaxProfiles.Remove(profile);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
