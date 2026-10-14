namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class BrokerageScopeChangeReader(BrokerageSyncDbContext db) : IAccountScopeChangeReader
{
    // A credential records when it was linked but not when it was deactivated; removal is only
    // approximated for bank accounts (BankAccountScopeChangeReader) until per-source rows exist (R8).
    public async Task<IReadOnlyList<AccountScopeChange>> GetChangesSinceAsync(
        Guid userId, DateTime sinceUtc, CancellationToken ct = default)
    {
        var ibkr = await db.IBKRCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.UserId == userId && c.CreatedAt >= sinceUtc)
            .Select(c => c.CreatedAt).ToListAsync(ct);
        var flex = await db.IBKRFlexCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.UserId == userId && c.CreatedAt >= sinceUtc)
            .Select(c => c.CreatedAt).ToListAsync(ct);
        var inzhur = await db.InzhurCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.UserId == userId && c.CreatedAt >= sinceUtc)
            .Select(c => c.CreatedAt).ToListAsync(ct);

        return
        [
            .. ibkr.Select(at => new AccountScopeChange("ibkr", AccountScopeChangeKinds.Linked, at)),
            .. flex.Select(at => new AccountScopeChange("ibkr-flex", AccountScopeChangeKinds.Linked, at)),
            .. inzhur.Select(at => new AccountScopeChange("inzhur", AccountScopeChangeKinds.Linked, at)),
        ];
    }
}
