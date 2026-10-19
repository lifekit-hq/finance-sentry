namespace FinanceSentry.Modules.BankSync.Infrastructure.Services;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class BankAccountScopeChangeReader(BankSyncDbContext db) : IAccountScopeChangeReader
{
    public string WealthSourceId => WealthSourceIds.Banking;

    public async Task<IReadOnlyList<AccountScopeChange>> GetChangesSinceAsync(
        Guid userId, DateTime sinceUtc, CancellationToken ct = default)
    {
        // The active-account filter must go too: a removed account is an inactive row, so it would never be seen.
        var rows = await db.BankAccounts
            .IgnoreQueryFilters([OwnerQueryFilter.Name, BankSyncDbContext.AccountActiveFilterName])
            .Where(a => a.UserId == userId && (a.CreatedAt >= sinceUtc || (!a.IsActive && a.UpdatedAt >= sinceUtc)))
            .Select(a => new { a.Provider, a.IsActive, a.CreatedAt, a.UpdatedAt })
            .ToListAsync(ct);

        var changes = new List<AccountScopeChange>();
        foreach (var a in rows)
        {
            if (a.CreatedAt >= sinceUtc)
                changes.Add(new AccountScopeChange(a.Provider, AccountScopeChangeKinds.Linked, a.CreatedAt));

            // Approximate: the row's last update stands in for the deactivation time until per-source rows exist (R8).
            if (!a.IsActive && a.UpdatedAt >= sinceUtc)
                changes.Add(new AccountScopeChange(a.Provider, AccountScopeChangeKinds.Removed, a.UpdatedAt!.Value));
        }

        return changes;
    }
}
