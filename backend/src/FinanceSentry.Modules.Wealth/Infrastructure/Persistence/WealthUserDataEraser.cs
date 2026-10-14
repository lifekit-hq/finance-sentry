namespace FinanceSentry.Modules.Wealth.Infrastructure.Persistence;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Migrations;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Erases a person's Wealth data. Beyond the owned-entity sweep, M005's backup of the net-worth snapshots it
/// corrected is raw SQL, outside the EF model, and holds a full copy of each snapshot by <c>UserId</c>: without this
/// the person's balances would survive the purge there.
/// </summary>
internal sealed class WealthUserDataEraser(WealthDbContext db) : IUserDataEraser
{
    public string Module => "Wealth";

    public async Task<int> EraseAsync(Guid userId, CancellationToken ct = default)
    {
        var removed = await db.Database.ExecuteSqlRawAsync(
            $"DELETE FROM {M005_CorrectInzhurNetWorthHistory.BackupTable} WHERE \"UserId\" = {{0}}", [userId], ct);

        return removed + await OwnedRowEraser.EraseAsync(db, userId, ct);
    }
}
