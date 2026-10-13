namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class AssetPriceTargetRepository(ResearchDbContext db) : IAssetPriceTargetRepository
{
    public async Task<AssetPriceTarget?> GetAsync(Guid userId, string symbol, CancellationToken ct = default)
    {
        var upper = symbol.Trim().ToUpperInvariant();
        return await db.AssetPriceTargets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Symbol == upper, ct);
    }

    // Same shape as AssetLedgerReadRepository.UpsertAsync: one INSERT ... ON CONFLICT over the unique
    // (UserId, Symbol) index, so two saves of one target update the same row instead of the loser failing.
    public async Task UpsertAsync(AssetPriceTarget target, CancellationToken ct = default)
    {
        target.Symbol = target.Symbol.Trim().ToUpperInvariant();
        if (target.Id == Guid.Empty)
        {
            target.Id = Guid.NewGuid();
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO research.asset_price_targets
                ("Id", "UserId", "Symbol", "TargetPrice", "Currency", "UpdatedAt")
            VALUES ({target.Id}, {target.UserId}, {target.Symbol}, {target.TargetPrice}, {target.Currency}, {target.UpdatedAt})
            ON CONFLICT ("UserId", "Symbol") DO UPDATE SET
                "TargetPrice" = EXCLUDED."TargetPrice",
                "Currency" = EXCLUDED."Currency",
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            """,
            ct);
    }

    public async Task<bool> DeleteAsync(Guid userId, string symbol, CancellationToken ct = default)
    {
        var upper = symbol.Trim().ToUpperInvariant();
        var existing = await db.AssetPriceTargets
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Symbol == upper, ct);
        if (existing is null)
        {
            return false;
        }

        db.AssetPriceTargets.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
