namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Compares the stored macro calendar with <see cref="MacroCalendarSchedule"/> by release key (#1091) and says what
/// the sync must change. Pure, so the decision is testable without a database. Rows the schedule does not know are
/// left alone; a key stored twice keeps one row (the one already on the scheduled date, else the oldest) and drops
/// the rest, so one release is never announced twice.
/// </summary>
public static class MacroCalendarReconciler
{
    public static MacroCalendarSyncPlan Plan(
        IReadOnlyCollection<MacroEvent> stored, IReadOnlyList<MacroEvent> schedule, DateTimeOffset now)
    {
        var inserts = new List<MacroEvent>();
        var corrections = new List<MacroCalendarCorrection>();
        var deletes = new List<MacroEvent>();
        var flags = new List<MacroCalendarFlag>();

        var byKey = stored
            .GroupBy(e => (MacroEventTiming.Key(e), Region: e.Region.ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var expected in schedule)
        {
            var key = MacroEventTiming.Key(expected);
            if (!byKey.TryGetValue((key, expected.Region.ToUpperInvariant()), out var rows))
            {
                inserts.Add(expected);
                continue;
            }

            var keeper = rows
                .OrderByDescending(r => r.EventDate == expected.EventDate && r.Event == expected.Event)
                .ThenBy(r => r.IngestedAt)
                .First();
            deletes.AddRange(rows.Where(r => !ReferenceEquals(r, keeper)));

            var storedAt = MacroEventTiming.EventAt(keeper);
            var actualAt = MacroEventTiming.EventAt(expected);

            // The #1091 failure: the row still reads as upcoming although the publisher has already released it,
            // so every lookahead reader would announce a print the market has already reacted to.
            if (storedAt > now && actualAt <= now)
            {
                flags.Add(new MacroCalendarFlag(key, keeper.Id, storedAt, actualAt));
            }

            if (keeper.EventDate != expected.EventDate
                || keeper.EventTime != expected.EventTime
                || keeper.Event != expected.Event
                || keeper.Importance != expected.Importance
                || keeper.Source != expected.Source)
            {
                corrections.Add(new MacroCalendarCorrection(keeper, expected));
            }
        }

        return new MacroCalendarSyncPlan(inserts, corrections, deletes, flags);
    }
}

/// <summary>What one sync pass changes.</summary>
public sealed record MacroCalendarSyncPlan(
    IReadOnlyList<MacroEvent> Inserts,
    IReadOnlyList<MacroCalendarCorrection> Corrections,
    IReadOnlyList<MacroEvent> Deletes,
    IReadOnlyList<MacroCalendarFlag> Flags);

/// <summary>A stored row whose date, time, name, importance or source differs from the schedule.</summary>
public sealed record MacroCalendarCorrection(MacroEvent Stored, MacroEvent Expected)
{
    public void Apply()
    {
        Stored.EventDate = Expected.EventDate;
        Stored.EventTime = Expected.EventTime;
        Stored.Event = Expected.Event;
        Stored.Importance = Expected.Importance;
        Stored.Source = Expected.Source;
    }
}

/// <summary>A row still dated in the future although the schedule says the release is already out.</summary>
public sealed record MacroCalendarFlag(string Key, Guid EventId, DateTimeOffset StoredAt, DateTimeOffset ReleasedAt);

/// <summary>The outcome of <see cref="IMacroCalendarService.SyncAsync"/>.</summary>
public sealed record MacroCalendarSyncResult(
    int Inserted, int Corrected, int Removed, IReadOnlyList<MacroCalendarFlag> PastButUpcoming);
