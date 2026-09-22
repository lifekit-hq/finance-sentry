namespace FinanceSentry.Modules.Events.Domain.Ports;

/// <summary>Scheduled macro events in a window (adapter over Research's macro calendar). Global, not user-scoped.</summary>
public interface IMacroEventReader
{
    Task<IReadOnlyList<MacroCalendarEntry>> QueryAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}

public sealed record MacroCalendarEntry(
    Guid Id, DateOnly Date, TimeOnly? Time, string Event, string Region, string Importance, string Source);
