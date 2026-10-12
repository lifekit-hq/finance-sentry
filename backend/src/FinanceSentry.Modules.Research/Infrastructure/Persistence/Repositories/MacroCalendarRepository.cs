namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class MacroCalendarRepository(ResearchDbContext db) : IMacroCalendarRepository
{
    public async Task<IReadOnlyList<MacroEvent>> QueryAsync(
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<string>? regions,
        string? minImportance,
        CancellationToken ct = default)
    {
        var q = db.MacroEvents.AsNoTracking()
            .Where(e => e.EventDate >= from && e.EventDate <= to);

        if (regions is { Count: > 0 })
        {
            var upper = regions.Select(r => r.ToUpperInvariant()).ToArray();
            q = q.Where(e => upper.Contains(e.Region));
        }

        if (!string.IsNullOrEmpty(minImportance))
        {
            var rank = ImportanceRank(minImportance);
            q = q.Where(e =>
                (e.Importance == MacroImportance.High ? 3 :
                    e.Importance == MacroImportance.Medium ? 2 : 1) >= rank);
        }

        return await q.OrderBy(e => e.EventDate).ThenBy(e => e.EventTime).ToListAsync(ct);
    }

    // One INSERT ... ON CONFLICT over the unique (EventDate, Region, Event) index: the write is atomic, so two
    // overlapping calendar refreshes update the same rows instead of the loser failing on the index, and no read
    // precedes it. It runs at once rather than at SaveChanges, and an event already tracked by this context is not
    // refreshed. A re-published event takes the new time, importance and source; RETURNING (xmax = 0) is true only
    // for a freshly inserted row, which is what the inserted count reports.
    public async Task<int> UpsertAsync(IReadOnlyCollection<MacroEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0)
        {
            return 0;
        }

        // ON CONFLICT cannot touch one row twice in a statement, so a repeated key keeps its first event.
        var incoming = events
            .GroupBy(e => (e.EventDate, e.Region, e.Event))
            .Select(g => g.First())
            .ToList();

        var ids = incoming.Select(e => e.Id).ToArray();
        var dates = incoming.Select(e => e.EventDate).ToArray();
        var times = incoming.Select(e => e.EventTime).ToArray();
        var names = incoming.Select(e => e.Event).ToArray();
        var regions = incoming.Select(e => e.Region).ToArray();
        var importances = incoming.Select(e => e.Importance).ToArray();
        var sources = incoming.Select(e => e.Source).ToArray();
        var ingestedAts = incoming.Select(e => e.IngestedAt).ToArray();

        var inserted = await db.Database.SqlQuery<bool>(
            $"""
            INSERT INTO research.macro_events AS t
                ("Id", "EventDate", "EventTime", "Event", "Region", "Importance", "Source", "IngestedAt")
            SELECT * FROM unnest(
                {ids}::uuid[], {dates}::date[], {times}::time[], {names}::varchar[], {regions}::varchar[],
                {importances}::varchar[], {sources}::varchar[], {ingestedAts}::timestamptz[])
            ON CONFLICT ("EventDate", "Region", "Event") DO UPDATE SET
                "EventTime" = EXCLUDED."EventTime",
                "Importance" = EXCLUDED."Importance",
                "Source" = EXCLUDED."Source"
            RETURNING (xmax = 0) AS "Value"
            """).ToListAsync(ct);

        return inserted.Count(isNew => isNew);
    }

    private static int ImportanceRank(string importance) => importance.ToLowerInvariant() switch
    {
        "high" => 3,
        "medium" => 2,
        _ => 1,
    };
}
