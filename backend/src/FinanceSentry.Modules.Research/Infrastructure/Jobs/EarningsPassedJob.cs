namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily Hangfire job: the post-event counterpart of <see cref="EarningsAheadJob"/>. When a thesis ticker, a trigger proxy
/// or an Active candidate's ticker had an earnings date inside the last <see cref="LookbackDays"/> days (the same
/// calendar feed and window the earnings-ahead job reads to resolve its alerts), it raises the thesis-review notice
/// (<see cref="ReviewDueRaiser"/>, source <c>earnings</c>, reference = ticker and date) so Ledger re-reads the thesis against
/// the numbers. Digest lane: the notice is held for the pulse. The window is wider than a day so a restarted scheduler or a
/// missed run loses nothing, and the generator's once-only identity makes the overlap a no-op. A crypto asset ticker is left
/// out (no earnings; the bare symbol resolves to an unrelated listed name), and the calendar feed is an external
/// dependency with no contract: a failed fetch is no signal this run, never a job failure. Coverage names are read too
/// (<see cref="LookaheadUniverse.CoverageTickersByPriorityAsync"/>): passed earnings are the event the coverage review acts on.
/// </summary>
public sealed class EarningsPassedJob(
    IBankingTotalsReader banking,
    ICryptoHoldingsReader crypto,
    LookaheadUniverse universe,
    IEarningsCalendarService earningsCalendar,
    ReviewDueRaiser reviewDue,
    ILogger<EarningsPassedJob> logger)
{
    /// <summary>The Hangfire recurring-job id this detector is scheduled under.</summary>
    public const string RecurringJobId = "earnings-passed";

    /// <summary>How far back a passed earnings date still counts (matches the earnings-ahead job's resolve window).</summary>
    public const int LookbackDays = 5;

    public Task ExecuteAsync(CancellationToken ct = default) => ExecuteAsync(DateTime.UtcNow, ct);

    /// <summary>Overload taking the reference instant explicitly, so tests aren't at the mercy of the day they run on.</summary>
    public async Task ExecuteAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var userIds = await banking.GetActiveUserIdsAsync(ct);
        if (userIds.Count == 0)
        {
            logger.LogDebug("EarningsPassed: no active users found, skipping.");
            return;
        }

        var today = DateOnly.FromDateTime(nowUtc);
        foreach (var userId in userIds)
        {
            try
            {
                await ProcessUserAsync(userId, today, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "EarningsPassed: error processing user {UserId}", userId);
            }
        }
    }

    private async Task ProcessUserAsync(Guid userId, DateOnly today, CancellationToken ct)
    {
        var subjects = await reviewDue.LoadSubjectsAsync(userId, ct);
        subjects.TrackCoverage(await universe.CoverageTickersByPriorityAsync(userId, ct));
        var cryptoAssets = (await crypto.GetHoldingsAsync(userId, ct))
            .Select(h => h.Asset)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tickers = subjects.WatchedTickers.Where(t => !cryptoAssets.Contains(t)).ToList();
        if (tickers.Count == 0)
        {
            return;
        }

        IReadOnlyList<EarningsEvent> events;
        try
        {
            events = await earningsCalendar.GetForTickersAsync(
                tickers, today.AddDays(-LookbackDays), today.AddDays(-1), EarningsEventType.Earnings, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "EarningsPassed: earnings-calendar fetch failed for user {UserId}", userId);
            return;
        }

        foreach (var evt in events)
        {
            await reviewDue.RaiseAsync(
                userId, subjects, evt.Ticker, ReviewDueSource.Earnings,
                $"{evt.Ticker.ToUpperInvariant()}:{evt.EventDate:yyyy-MM-dd}",
                [$"earnings:{evt.Ticker.ToUpperInvariant()}:{evt.EventDate:yyyy-MM-dd}"],
                $"earnings date {evt.EventDate:yyyy-MM-dd} has passed", ct);
        }

        await reviewDue.CompleteAsync(userId, subjects, ct);
    }
}
