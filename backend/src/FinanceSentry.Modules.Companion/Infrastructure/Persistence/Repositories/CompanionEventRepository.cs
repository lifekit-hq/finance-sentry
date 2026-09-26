namespace FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class CompanionEventRepository(CompanionDbContext db) : ICompanionEventRepository
{
    private const int MaxLimit = 200;

    public async Task<bool> InsertIfNewAsync(CompanionEvent evt, CancellationToken ct = default)
    {
        var exists = await db.Events.AsNoTracking().AnyAsync(e => e.DedupKey == evt.DedupKey, ct);
        if (exists)
        {
            return false;
        }

        db.Events.Add(evt);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Lost a race on the unique DedupKey — treat as already-captured.
            db.Entry(evt).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<CompanionEvent>> ListByDispositionAsync(
        Guid userId, IReadOnlyCollection<EventDisposition> dispositions, int limit, CancellationToken ct = default)
    {
        var effective = Math.Clamp(limit, 1, MaxLimit);
        return await db.Events.AsNoTracking()
            .Where(e => e.UserId == userId && dispositions.Contains(e.Disposition))
            .OrderByDescending(e => e.OccurredAt)
            .Take(effective)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CompanionEvent>> ListRealtimePendingAsync(int limit, CancellationToken ct = default)
    {
        var effective = Math.Clamp(limit, 1, MaxLimit);
        return await db.Events
            .Where(e => e.Disposition == EventDisposition.Pending || e.Disposition == EventDisposition.DeferredQuietHours
                || e.Disposition == EventDisposition.SuppressedByRateLimit)
            .OrderBy(e => e.OccurredAt)
            .Take(effective)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CompanionEvent>> ListHeldForDigestAsync(Guid userId, CancellationToken ct = default)
        => await db.Events
            .Where(e => e.UserId == userId && e.Disposition == EventDisposition.HeldForDigest)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListHeldForDigestUserIdsAsync(CancellationToken ct = default)
        => await db.Events.AsNoTracking()
            .Where(e => e.Disposition == EventDisposition.HeldForDigest)
            .Select(e => e.UserId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<int> CountDispatchedSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default)
        => await db.Events.AsNoTracking()
            .CountAsync(e => e.UserId == userId && e.DispatchedAt != null && e.DispatchedAt >= since, ct);

    public async Task<CompanionEvent?> GetAsync(Guid id, CancellationToken ct = default)
        => await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task UpdateAsync(CompanionEvent evt, CancellationToken ct = default)
    {
        db.Events.Update(evt);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> MarkDeliveredAsync(
        Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        var rows = await db.Events
            .Where(e => e.UserId == userId && ids.Contains(e.Id) && e.Disposition != EventDisposition.Delivered)
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            row.Disposition = EventDisposition.Delivered;
            row.DeliveredAt = now;
        }

        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    public async Task<IReadOnlyList<CompanionEvent>> ListByDedupKeysAsync(
        Guid userId, IReadOnlyCollection<string> dedupKeys, CancellationToken ct = default)
    {
        if (dedupKeys.Count == 0)
        {
            return [];
        }

        return await db.Events.AsNoTracking()
            .Where(e => e.UserId == userId && dedupKeys.Contains(e.DedupKey))
            .ToListAsync(ct);
    }
}
