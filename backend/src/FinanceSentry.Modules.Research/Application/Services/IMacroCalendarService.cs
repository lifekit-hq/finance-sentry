namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;

public interface IMacroCalendarService
{
    Task<IReadOnlyList<MacroEvent>> QueryAsync(
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<string>? regions,
        string? minImportance,
        CancellationToken ct = default);

    /// <summary>
    /// Reconciles the stored calendar with <see cref="MacroCalendarSchedule"/> (#1091): inserts missing releases,
    /// corrects misdated ones, drops a release stored twice, and reports every row that still read as upcoming
    /// although the release was already out at <paramref name="now"/>.
    /// </summary>
    Task<MacroCalendarSyncResult> SyncAsync(DateTimeOffset now, CancellationToken ct = default);
}
