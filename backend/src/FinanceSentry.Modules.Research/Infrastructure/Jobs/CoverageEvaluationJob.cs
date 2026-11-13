namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Modules.Research.Application.Services.Coverage;
using Hangfire;

/// <summary>
/// Daily evaluation of the coverage universe (discovery funnel stage 1): seeds it from holdings and the watchlist,
/// applies the add and remove rules and journals each change. Runs after the 00:00 opportunity scan and candidate
/// expiry, whose results it reads. Deterministic, no model call. A user it could not evaluate makes the job end
/// Failed after the others are saved, never green-and-stale.
/// </summary>
public sealed class CoverageEvaluationJob(CoverageEvaluator evaluator, TimeProvider clock)
{
    public const string RecurringJobId = "coverage-evaluation";

    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken ct = default)
        => await evaluator.EvaluateAllAsync(clock.GetUtcNow(), ct);
}
