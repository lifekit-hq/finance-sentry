namespace FinanceSentry.Modules.Events.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class EventVerdictRepository(EventsDbContext db) : IEventVerdictRepository
{
    public async Task UpsertAsync(EventVerdict verdict, CancellationToken ct = default)
    {
        var existing = await db.Verdicts.FirstOrDefaultAsync(
            v => v.UserId == verdict.UserId && v.CompanionEventId == verdict.CompanionEventId, ct);

        if (existing is null)
        {
            db.Verdicts.Add(verdict);
        }
        else
        {
            existing.AlertId = verdict.AlertId;
            existing.Verdict = verdict.Verdict;
            existing.Notified = verdict.Notified;
            existing.RecordedAt = verdict.RecordedAt;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EventVerdict>> ListByAlertIdsAsync(
        Guid userId, IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default)
    {
        if (alertIds.Count == 0)
        {
            return [];
        }

        return await db.Verdicts.AsNoTracking()
            .Where(v => v.UserId == userId && v.AlertId != null && alertIds.Contains(v.AlertId!.Value))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EventVerdict>> ListByCompanionEventIdsAsync(
        Guid userId, IReadOnlyCollection<Guid> companionEventIds, CancellationToken ct = default)
    {
        if (companionEventIds.Count == 0)
        {
            return [];
        }

        return await db.Verdicts.AsNoTracking()
            .Where(v => v.UserId == userId && companionEventIds.Contains(v.CompanionEventId))
            .ToListAsync(ct);
    }
}
