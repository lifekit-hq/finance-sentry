namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Modules.Research.Application.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily reconcile of the macro calendar against <see cref="MacroCalendarSchedule"/> (#1091). Kept under its original
/// name and Hangfire id; it used to seed an empty table only.
/// </summary>
public sealed class MacroCalendarSeedJob(
    IMacroCalendarService svc,
    TimeProvider clock,
    ILogger<MacroCalendarSeedJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var result = await svc.SyncAsync(clock.GetUtcNow(), ct);
        if (result.Inserted + result.Corrected + result.Removed > 0)
        {
            logger.LogInformation(
                "MacroCalendarSeedJob inserted {Inserted}, corrected {Corrected}, removed {Removed} events ({Flagged} past but still upcoming)",
                result.Inserted, result.Corrected, result.Removed, result.PastButUpcoming.Count);
        }
    }
}
