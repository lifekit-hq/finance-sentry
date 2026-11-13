namespace FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;

using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>What one dataset's refresh did.</summary>
public sealed record ReferenceRefreshOutcome(string Source, bool Succeeded, int MemberCount, string? Error);

/// <summary>One or more datasets failed to refresh; each is already logged and recorded as a failed run row.</summary>
public sealed class ReferenceUniverseRefreshException(IReadOnlyList<ReferenceRefreshOutcome> failures)
    : Exception($"Reference universe refresh failed for: {string.Join("; ", failures.Select(f => $"{f.Source} ({f.Error})"))}")
{
    public IReadOnlyList<ReferenceRefreshOutcome> Failures { get; } = failures;
}

/// <summary>
/// Refreshes every reference dataset, each isolated from the others. A failure is loud three ways: an error
/// log line, a <see cref="ReferenceRefreshRun"/> row carrying the reason, and an exception out of
/// <see cref="RefreshAsync"/> once every dataset has been tried (so the Hangfire job ends Failed). A failed
/// dataset keeps its previous members - stale, but flagged by the run row and by the reader's status - and a
/// payload that would shrink a list below <see cref="ReferenceUniverseOptions.MinRetainedFraction"/> is refused
/// rather than applied.
/// </summary>
public sealed class ReferenceUniverseRefresher(
    IEnumerable<IReferenceListFeed> feeds,
    IReferenceUniverseRepository repository,
    IOptions<ReferenceUniverseOptions> options,
    TimeProvider time,
    ILogger<ReferenceUniverseRefresher> logger)
{
    public async Task<IReadOnlyList<ReferenceRefreshOutcome>> RefreshAsync(CancellationToken ct = default)
    {
        var outcomes = new List<ReferenceRefreshOutcome>();
        foreach (var feed in feeds)
        {
            outcomes.Add(await RefreshOneAsync(feed, ct));
        }

        var failures = outcomes.Where(o => !o.Succeeded).ToList();
        if (failures.Count > 0)
        {
            throw new ReferenceUniverseRefreshException(failures);
        }

        return outcomes;
    }

    private async Task<ReferenceRefreshOutcome> RefreshOneAsync(IReferenceListFeed feed, CancellationToken ct)
    {
        var started = time.GetUtcNow();
        try
        {
            var snapshots = await feed.FetchAsync(ct);
            var desired = snapshots
                .SelectMany(s => s.Members.Select(m => new ReferenceListMember
                {
                    ListKey = s.ListKey,
                    Ticker = m.Ticker,
                    Name = m.Name,
                    Source = feed.Source,
                    AddedAt = started,
                }))
                .ToList();

            await GuardAgainstShrinkageAsync(feed.Source, desired.Count, ct);

            var count = await repository.ReplaceSourceAsync(feed.Source, desired, ct);
            await repository.AddRunAsync(new ReferenceRefreshRun
            {
                Source = feed.Source,
                StartedAt = started,
                FinishedAt = time.GetUtcNow(),
                Succeeded = true,
                MemberCount = count,
            }, ct);

            logger.LogInformation(
                "Reference universe refresh {Source} succeeded: {Lists} lists, {Members} members",
                feed.Source, snapshots.Count, count);
            return new ReferenceRefreshOutcome(feed.Source, true, count, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Reference universe refresh {Source} FAILED - its lists stay at the previous refresh", feed.Source);
            await RecordFailureAsync(feed.Source, started, ex, ct);
            return new ReferenceRefreshOutcome(feed.Source, false, 0, ex.Message);
        }
    }

    private async Task GuardAgainstShrinkageAsync(string source, int incoming, CancellationToken ct)
    {
        // Theme lists are edited by hand in configuration, so a legitimate edit may shrink them freely.
        if (source == ReferenceSources.Themes)
        {
            return;
        }

        var previous = await repository.CountBySourceAsync(source, ct);
        var floor = previous * options.Value.MinRetainedFraction;
        if (previous > 0 && incoming < floor)
        {
            throw new ReferenceFeedException(
                $"refresh would shrink {source} from {previous} to {incoming} members (below {options.Value.MinRetainedFraction:P0}); refusing to apply it");
        }
    }

    private async Task RecordFailureAsync(string source, DateTimeOffset started, Exception ex, CancellationToken ct)
    {
        try
        {
            await repository.AddRunAsync(new ReferenceRefreshRun
            {
                Source = source,
                StartedAt = started,
                FinishedAt = time.GetUtcNow(),
                Succeeded = false,
                Error = Truncate($"{ex.GetType().Name}: {ex.Message}"),
            }, ct);
        }
        catch (Exception logEx) when (logEx is not OperationCanceledException)
        {
            // The failure itself is already logged and still surfaces through the job's exception.
            logger.LogError(logEx, "Could not record the failed reference universe refresh of {Source}", source);
        }
    }

    private static string Truncate(string s) => s.Length <= 1900 ? s : s[..1900];
}
