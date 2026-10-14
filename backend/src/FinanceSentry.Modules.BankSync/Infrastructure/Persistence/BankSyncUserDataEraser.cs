namespace FinanceSentry.Modules.BankSync.Infrastructure.Persistence;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Erases a person's BankSync data: withdraws each account's recurring sync schedule first (Hangfire keeps it in its
/// own storage, outside this context), then deletes every owned row, removed accounts and archived transactions
/// included.
/// </summary>
internal sealed class BankSyncUserDataEraser(BankSyncDbContext db, IRecurringJobManager recurringJobs) : IUserDataEraser
{
    public string Module => "BankSync";

    public async Task<int> EraseAsync(Guid userId, CancellationToken ct = default)
    {
        var accountIds = await db.BankAccounts.IgnoreQueryFilters()
            .Where(a => a.UserId == userId)
            .Select(a => a.Id)
            .ToListAsync(ct);

        foreach (var accountId in accountIds)
        {
            recurringJobs.RemoveIfExists(SyncAccountJob.IdFor(accountId));
        }

        return await OwnedRowEraser.EraseAsync(db, userId, ct);
    }
}
