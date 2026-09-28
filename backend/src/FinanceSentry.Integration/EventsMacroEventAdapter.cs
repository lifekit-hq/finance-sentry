namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>Feature 049 - <see cref="IMacroEventReader"/> over Research's published <see cref="IMacroCalendarReader"/> port (#673; all regions, every importance).</summary>
public sealed class EventsMacroEventAdapter(IMacroCalendarReader macro) : IMacroEventReader
{
    public async Task<IReadOnlyList<MacroCalendarEntry>> QueryAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await macro.QueryAsync(from, to, ct);
        return rows
            .Select(m => new MacroCalendarEntry(m.Id, m.EventDate, m.EventTime, m.Event, m.Region, m.Importance, m.Source))
            .ToList();
    }
}
