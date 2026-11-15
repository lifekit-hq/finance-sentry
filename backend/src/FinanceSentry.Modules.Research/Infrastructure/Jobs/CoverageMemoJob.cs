namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Modules.Research.Application.Services.Coverage;
using Hangfire;

/// <summary>
/// Daily research memos on the coverage entrants (discovery funnel stage 6), after the 01:10 coverage evaluation whose
/// adds it writes up. Does nothing and spends nothing unless <c>Coverage:MemoMode</c> is set; capped per day and per
/// week (see <see cref="CoverageMemoWriter"/>). A failed model call ends the job Failed after the memos already written
/// are saved.
/// </summary>
public sealed class CoverageMemoJob(CoverageMemoWriter writer, TimeProvider clock)
{
    public const string RecurringJobId = "coverage-memos";

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct = default)
        => await writer.WriteAllAsync(clock.GetUtcNow(), ct);
}
