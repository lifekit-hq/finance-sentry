using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.CryptoSync.Application.Services;

/// <summary>
/// <see cref="ICryptoIncomeReader"/> over the earn and staking rewards and airdrops the venues reported
/// (<see cref="CryptoFlowEvent"/>). A reward is paid in the coin, so its USD value is the amount at the venue price
/// taken when the row was read; a row with no price is returned unvalued, never as a zero.
/// </summary>
public sealed class CryptoIncomeReader(CryptoSyncDbContext db) : ICryptoIncomeReader
{
    public async Task<IReadOnlyList<IncomeReceipt>> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default)
    {
        var fromTime = from.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        var rows = await db.CryptoFlowEvents.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(e => e.UserId == userId
                && e.OccurredAt > fromTime
                && (e.ProviderType == CryptoFlowTypes.Reward || e.ProviderType == CryptoFlowTypes.Airdrop))
            .Select(e => new { e.Unit, e.Amount, e.UnitPriceUsd, e.OccurredAt })
            .ToListAsync(ct);

        return rows
            .OrderBy(r => r.OccurredAt)
            .Select(r => new IncomeReceipt(
                r.Unit.Trim().ToUpperInvariant(),
                IncomeReceiptKind.Staking,
                DateOnly.FromDateTime(r.OccurredAt),
                r.UnitPriceUsd is { } price ? r.Amount * price : null,
                IsWithholding: false))
            .ToList();
    }
}
