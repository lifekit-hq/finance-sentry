namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using Microsoft.Extensions.Logging;

/// <summary>
/// Re-probes news sources the health tracker auto-retired and returns the ones that can fetch again to
/// service (spec 047). Without it, retirement is permanent: <see cref="NewsIngestionJob"/> only walks
/// enabled sources, and nothing else re-enables a row — so a source that failed for six hours stayed
/// dark for six weeks even after the cause was fixed in code (issue #318).
/// <para>
/// A probe is a real ingestion, not a ping: articles it fetches are tagged and inserted like any other
/// run. A probe that fails is expected and stays quiet — it refreshes the recorded failure reason so a
/// diagnosis starts from today's error, but leaves <see cref="NewsSource.ConsecutiveFailures"/> alone
/// (that counter means "failures while in service") and raises no alert, since the disable alert for
/// this source already fired.
/// </para>
/// </summary>
public sealed class NewsSourceRecoveryJob(
    INewsSourceRepository sources,
    INewsRepository newsRepo,
    NewsSourceFetcher fetcher,
    ILogger<NewsSourceRecoveryJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var disabled = await sources.ListDisabledAsync(ct);
        if (disabled.Count == 0)
        {
            return;
        }

        var revived = 0;
        foreach (var source in disabled)
        {
            if (await ProbeAsync(source, ct))
            {
                revived++;
            }
        }

        logger.LogInformation(
            "NewsSourceRecoveryJob probed {Probed} retired sources, revived {Revived}", disabled.Count, revived);
    }

    private async Task<bool> ProbeAsync(NewsSource source, CancellationToken ct)
    {
        IReadOnlyList<NewsArticle> articles;
        int inserted;

        // Only the probe itself is guarded: a failure to persist the outcome is an infrastructure
        // fault, not evidence about the source, and must not be recorded as its failure reason.
        try
        {
            articles = await fetcher.FetchAsync(source, ct);
            inserted = await newsRepo.InsertNewAsync(articles, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            source.LastFailureReason = ex.Message;
            await sources.UpdateAsync(source, ct);

            logger.LogInformation(
                "News source {Source} is still failing after {Consecutive} failures: {Reason}",
                source.Name, source.ConsecutiveFailures, ex.Message);
            return false;
        }

        NewsSourceHealthTracker.ClearFailures(source);
        NewsSourceHealthTracker.RecordSuccess(source);
        await sources.UpdateAsync(source, ct);

        logger.LogInformation(
            "News source {Source} recovered and returned to service: {Fetched} fetched, {Inserted} new",
            source.Name, articles.Count, inserted);
        return true;
    }
}
