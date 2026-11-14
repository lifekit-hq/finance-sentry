namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Modules.Research.Application.Services.Screens;
using Hangfire;

/// <summary>
/// Nightly discovery-funnel screens (stage 5): fundamentals, relative strength and dated catalyst over the reference
/// universe, each nominating into the candidate path with its screen tag. Runs after Radar's 23:00 compute and before
/// the 00:00 opportunity scan and the 01:10 coverage evaluation, which decides what the nominations earn. Deterministic,
/// no model call. A screen that could not run makes the job end Failed after the others have nominated, never green-and-stale.
/// </summary>
public sealed class ScreenNominationJob(ScreenNominator nominator, TimeProvider clock)
{
    public const string RecurringJobId = "screen-nomination";

    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken ct = default)
        => await nominator.RunAsync(clock.GetUtcNow(), ct);
}
