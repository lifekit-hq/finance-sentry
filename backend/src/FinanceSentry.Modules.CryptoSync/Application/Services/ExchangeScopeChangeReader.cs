namespace FinanceSentry.Modules.CryptoSync.Application.Services;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class ExchangeScopeChangeReader(CryptoSyncDbContext db) : IAccountScopeChangeReader
{
    // CreatedAt is re-stamped on reactivation, so a relinked exchange counts as newly linked.
    // Removal is approximated for bank accounts only until per-source rows exist (R8).
    public async Task<IReadOnlyList<AccountScopeChange>> GetChangesSinceAsync(
        Guid userId, DateTime sinceUtc, CancellationToken ct = default)
    {
        var rows = await db.ExchangeCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.UserId == userId && c.IsActive && c.CreatedAt >= sinceUtc)
            .Select(c => new { c.Provider, c.CreatedAt })
            .ToListAsync(ct);
        return [.. rows.Select(r => new AccountScopeChange(r.Provider, AccountScopeChangeKinds.Linked, r.CreatedAt))];
    }
}
