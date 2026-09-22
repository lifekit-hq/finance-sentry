namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Research.Application.Services;

/// <summary>Feature 049 - <see cref="IMacroEventReader"/> over Research's macro calendar (all regions, every importance).</summary>
public sealed class EventsMacroEventAdapter(IMacroCalendarService macro) : IMacroEventReader
{
    public async Task<IReadOnlyList<MacroCalendarEntry>> QueryAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await macro.QueryAsync(from, to, null, null, ct);
        return rows
            .Select(m => new MacroCalendarEntry(m.Id, m.EventDate, m.EventTime, m.Event, m.Region, m.Importance, m.Source))
            .ToList();
    }
}
