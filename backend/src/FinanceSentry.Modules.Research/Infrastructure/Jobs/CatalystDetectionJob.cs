namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using Hangfire;

/// <summary>
/// Daily catalyst scan (discovery funnel stage 7): 8-K items 1.01, 2.02, 5.02 and 8.01 filed on reference-universe names, so a
/// name outside coverage can enter on a filing. Runs after the 00:00 opportunity scan and before the 01:10 coverage evaluation that
/// reads the candidates it nominates. Deterministic, no model call. A feed that cannot be read at all (full-text search and the
/// submissions fallback both down) or a user it could not handle makes the job end Failed, never green-and-stale.
/// </summary>
public sealed class CatalystDetectionJob(CatalystDetector detector)
{
    public const string RecurringJobId = "catalyst-detection";

    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken ct = default) => await detector.RunAsync(ct);
}

/// <summary>
/// Daily GDELT attention refresh (discovery funnel stage 7): article volume per theme phrase, stored as the attention measure the
/// scorecard and readers expose. A phrase GDELT will not answer makes the job end Failed after the others are stored.
/// </summary>
public sealed class ThemeAttentionRefreshJob(ThemeAttentionRefresher refresher)
{
    public const string RecurringJobId = "theme-attention-refresh";

    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken ct = default) => await refresher.RefreshAsync(ct);
}
