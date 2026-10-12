namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class AssetLedgerReadRepository(ResearchDbContext db) : IAssetLedgerReadRepository
{
    public async Task<AssetLedgerRead?> GetAsync(Guid userId, string symbol, CancellationToken ct = default)
    {
        var upper = symbol.Trim().ToUpperInvariant();
        return await db.AssetLedgerReads.AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Symbol == upper, ct);
    }

    // One INSERT ... ON CONFLICT over the unique (UserId, Symbol) index: the write is atomic, so two regenerations
    // of one read update the same row instead of the loser failing on the index, and no read precedes it. It runs
    // at once rather than at SaveChanges. The stored row keeps its Id and takes the new narrative, fingerprint and
    // generation time.
    public async Task UpsertAsync(AssetLedgerRead read, CancellationToken ct = default)
    {
        read.Symbol = read.Symbol.Trim().ToUpperInvariant();
        if (read.Id == Guid.Empty)
        {
            read.Id = Guid.NewGuid();
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO research.asset_ledger_reads
                ("Id", "UserId", "Symbol", "Narrative", "SourceFingerprint", "GeneratedAt")
            VALUES ({read.Id}, {read.UserId}, {read.Symbol}, {read.Narrative}, {read.SourceFingerprint}, {read.GeneratedAt})
            ON CONFLICT ("UserId", "Symbol") DO UPDATE SET
                "Narrative" = EXCLUDED."Narrative",
                "SourceFingerprint" = EXCLUDED."SourceFingerprint",
                "GeneratedAt" = EXCLUDED."GeneratedAt"
            """,
            ct);
    }
}
