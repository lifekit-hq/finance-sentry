namespace FinanceSentry.Modules.Events.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class EventVerdictRepository(EventsDbContext db) : IEventVerdictRepository
{
    // One INSERT ... ON CONFLICT on the unique (UserId, CompanionEventId) index: a later record replaces the verdict in
    // place (the row keeps its Id), so two records of one event leave one row instead of the loser failing on the
    // index, and no read precedes it. It runs at once and the verdict is not tracked by this context.
    public async Task UpsertAsync(EventVerdict verdict, CancellationToken ct = default)
        => await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO events.event_verdicts
                ("Id", "UserId", "CompanionEventId", "AlertId", "Verdict", "Notified", "RecordedAt")
            VALUES
                ({verdict.Id}, {verdict.UserId}, {verdict.CompanionEventId}, {verdict.AlertId}, {verdict.Verdict},
                 {verdict.Notified}, {verdict.RecordedAt.ToUniversalTime()})
            ON CONFLICT ("UserId", "CompanionEventId") DO UPDATE SET
                "AlertId" = EXCLUDED."AlertId",
                "Verdict" = EXCLUDED."Verdict",
                "Notified" = EXCLUDED."Notified",
                "RecordedAt" = EXCLUDED."RecordedAt"
            """,
            ct);

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
