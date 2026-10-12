namespace FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Companion.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class CompanionCaptureStateRepository(CompanionDbContext db) : ICompanionCaptureStateRepository
{
    public async Task<DateTimeOffset> GetWatermarkAsync(
        string source, DateTimeOffset floor, CancellationToken ct = default)
    {
        var row = await db.CaptureState.AsNoTracking().FirstOrDefaultAsync(s => s.Source == source, ct);
        return row?.Watermark ?? floor;
    }

    // One INSERT ... ON CONFLICT on the Source primary key, so two first captures of one source set the watermark
    // instead of the loser failing on the key.
    public async Task SetWatermarkAsync(string source, DateTimeOffset watermark, CancellationToken ct = default)
        => await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO companion.companion_capture_state ("Source", "Watermark")
            VALUES ({source}, {watermark.ToUniversalTime()})
            ON CONFLICT ("Source") DO UPDATE SET "Watermark" = EXCLUDED."Watermark"
            """,
            ct);
}
