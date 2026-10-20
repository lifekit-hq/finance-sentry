namespace FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class LedgerCorrectionRepository(CompanionDbContext db) : ILedgerCorrectionRepository
{
    public async Task<LedgerCorrection> AddAsync(LedgerCorrection correction, CancellationToken ct = default)
    {
        db.LedgerCorrections.Add(correction);
        await db.SaveChangesAsync(ct);
        return correction;
    }

    public async Task<IReadOnlyList<LedgerCorrection>> ListAsync(
        Guid userId, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct = default)
        => await db.LedgerCorrections.AsNoTracking()
            .Where(c => c.UserId == userId && c.CorrectedAt >= from && c.CorrectedAt < to)
            .OrderByDescending(c => c.CorrectedAt)
            .ThenByDescending(c => c.Id)
            .Take(Math.Clamp(limit, 1, LedgerCorrectionLimits.MaxListLimit))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DateTimeOffset>> ListCorrectedAtAsync(Guid userId, CancellationToken ct = default)
        => await db.LedgerCorrections.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.CorrectedAt)
            .ToListAsync(ct);
}
