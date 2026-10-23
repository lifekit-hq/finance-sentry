namespace FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// <see cref="ICoverageHoleReader"/> over the sentinel's <c>CoverageHole</c> rows. Opts out of the Owner filter and
/// keeps its own <c>UserId</c> predicate: the readers that consume holes (budgets, estimates) also run in jobs with no
/// person in scope.
/// </summary>
public sealed class CoverageHoleReader(BankSyncDbContext db) : ICoverageHoleReader
{
    public async Task<IReadOnlyList<CoverageHoleSpan>> GetHolesAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(h => h.UserId == userId && h.From <= to && (h.To == null || h.To >= from))
            .OrderBy(h => h.From)
            .ToListAsync(ct);

        return [.. rows.Select(h => new CoverageHoleSpan(h.AccountId, h.From, h.To))];
    }
}
