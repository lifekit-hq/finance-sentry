namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public class MacroCalendarService(
    IMacroCalendarRepository repo,
    ResearchDbContext db,
    ILogger<MacroCalendarService> logger) : IMacroCalendarService
{
    public Task<IReadOnlyList<MacroEvent>> QueryAsync(
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<string>? regions,
        string? minImportance,
        CancellationToken ct = default)
        => repo.QueryAsync(from, to, regions, minImportance, ct);

    public async Task<MacroCalendarSyncResult> SyncAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        // The calendar used to be seeded only into an empty table, so a corrected date never reached a database that
        // already held the first, wrong seed (#1091). Every pass now reconciles against the schedule instead.
        var stored = await db.MacroEvents.ToListAsync(ct);
        var plan = MacroCalendarReconciler.Plan(stored, MacroCalendarSchedule.Events(), now);

        foreach (var flag in plan.Flags)
        {
            logger.LogWarning(
                "Macro event {Key} ({EventId}) was stored as upcoming at {StoredAt:o} but was released at {ReleasedAt:o}; correcting it",
                flag.Key, flag.EventId, flag.StoredAt, flag.ReleasedAt);
        }

        foreach (var correction in plan.Corrections)
        {
            logger.LogInformation(
                "Macro event {Key} corrected from {StoredDate} {StoredTime} to {ExpectedDate} {ExpectedTime}",
                MacroEventTiming.Key(correction.Expected),
                correction.Stored.EventDate, correction.Stored.EventTime,
                correction.Expected.EventDate, correction.Expected.EventTime);
            correction.Apply();
        }

        db.MacroEvents.RemoveRange(plan.Deletes);
        await db.SaveChangesAsync(ct);

        var inserted = plan.Inserts.Count > 0 ? await repo.UpsertAsync(plan.Inserts, ct) : 0;
        return new MacroCalendarSyncResult(inserted, plan.Corrections.Count, plan.Deletes.Count, plan.Flags);
    }
}
