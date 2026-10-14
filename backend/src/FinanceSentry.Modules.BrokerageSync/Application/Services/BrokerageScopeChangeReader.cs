namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class BrokerageScopeChangeReader(BrokerageSyncDbContext db) : IAccountScopeChangeReader
{
    // CreatedAt is re-stamped when a deactivated credential is relinked, so a relink counts as newly linked.
    // A credential records no deactivation time: removal is detected for bank accounts only
    // (BankAccountScopeChangeReader), and per-source rows (R8) resolve it for the rest.
    public async Task<IReadOnlyList<AccountScopeChange>> GetChangesSinceAsync(
        Guid userId, DateTime sinceUtc, CancellationToken ct = default)
    {
        var ibkr = await db.IBKRCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.UserId == userId && c.IsActive && c.CreatedAt >= sinceUtc)
            .Select(c => c.CreatedAt).ToListAsync(ct);
        var flex = await db.IBKRFlexCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.UserId == userId && c.IsActive && c.CreatedAt >= sinceUtc)
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
