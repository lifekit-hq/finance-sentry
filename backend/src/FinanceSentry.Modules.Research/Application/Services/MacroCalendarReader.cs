namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary><see cref="IMacroCalendarReader"/> impl over Research's own <see cref="IMacroCalendarService"/> (all regions, every importance).</summary>
public sealed class MacroCalendarReader(IMacroCalendarService macro, TimeProvider clock) : IMacroCalendarReader
{
    public async Task<IReadOnlyList<MacroCalendarItem>> QueryAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await macro.QueryAsync(from, to, null, null, ct);
        var now = clock.GetUtcNow();
        return rows
            .Select(m =>
            {
                var at = MacroEventTiming.EventAt(m);
                return new MacroCalendarItem(
                    m.Id, m.EventDate, m.EventTime, m.Event, m.Region, m.Importance, m.Source,
                    at, MacroEventTiming.StatusAt(at, now), MacroEventTiming.Key(m));
            })
            .ToList();
    }
}
