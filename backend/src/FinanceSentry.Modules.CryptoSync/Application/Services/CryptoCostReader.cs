using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.CryptoSync.Application.Services;

/// <summary>
/// <see cref="ICryptoCostReader"/> over the fills the venues reported (<see cref="Domain.CryptoTradeRecord"/>): each
/// fill's fee in USD at the fill's own time. A fill that reported a fee but could not value it is counted unvalued; one
/// that reported none (rows synced before fees were kept) is not a cost row at all.
/// </summary>
public sealed class CryptoCostReader(CryptoSyncDbContext db) : ICryptoCostReader
{
    public async Task<CostReading> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default)
    {
        var fromTime = from.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        var rows = await db.CryptoTrades.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.Timestamp > fromTime && t.Fee != null)
            .Select(t => new { t.FeeUsd, t.Timestamp })
            .ToListAsync(ct);

        var items = rows
            .Where(r => r.FeeUsd is not null)
            .OrderBy(r => r.Timestamp)
            .Select(r => new CostItem(CostKind.Commission, DateOnly.FromDateTime(r.Timestamp), r.FeeUsd!.Value))
            .ToList();
        return new CostReading(items, rows.Count(r => r.FeeUsd is null));
    }
}
