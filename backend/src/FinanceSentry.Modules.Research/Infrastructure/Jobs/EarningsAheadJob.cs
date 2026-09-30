namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily Hangfire job (T3, ledger-heartbeat design): raises an EarningsAhead alert for a book,
/// watchlist or thesis ticker (<see cref="LookaheadUniverse.EarningsTickersAsync"/>) with earnings
/// landing within <see cref="LookaheadDays"/> days, or an ex-dividend date in the same window. Thesis
/// tickers are in scope because a thesis is the strongest statement that a name's report matters
/// (#698: a thesis name reporting in the window was never looked up while it sat outside the book). Yahoo quoteSummary is an external dependency with no contract — a
/// missing field, a null date, or a failed fetch already come back as an empty result from
/// <see cref="IEarningsCalendarService"/>, so a provider hiccup here means no alert this run, never a
/// job failure. <see cref="IAlertGeneratorService.GenerateEarningsAheadAlertAsync"/> dedups per
/// (ticker, event type, event date), so re-running the job on the days leading up to the event never
/// re-alerts it — rare, low-noise by design (~0.1 fires/day).
/// </summary>
public sealed class EarningsAheadJob(
    IBankingTotalsReader banking,
    LookaheadUniverse universe,
    IEarningsCalendarService earningsCalendar,
    IAlertGeneratorService alerts,
    ILogger<EarningsAheadJob> logger)
{
    /// <summary>The Hangfire recurring-job id this detector is scheduled under.</summary>
    public const string RecurringJobId = "earnings-ahead";

    private const int LookaheadDays = 3;

    /// <summary>
    /// How far back to look for events whose date has just passed, so the deterministic
    /// (ticker, event type, event date) reference can be recomputed and resolved directly.
    /// </summary>
    private const int ResolveLookbackDays = 5;

    public Task ExecuteAsync(CancellationToken ct = default) => ExecuteAsync(DateTime.UtcNow, ct);

    /// <summary>Overload taking the reference instant explicitly, so tests aren't at the mercy of the day they run on.</summary>
    public async Task ExecuteAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var userIds = await banking.GetActiveUserIdsAsync(ct);
        if (userIds.Count == 0)
        {
            logger.LogDebug("EarningsAhead: no active users found, skipping.");
            return;
        }

        var today = DateOnly.FromDateTime(nowUtc);
        var horizon = today.AddDays(LookaheadDays);

        foreach (var userId in userIds)
        {
            try
            {
                await ProcessUserAsync(userId, today, horizon, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "EarningsAhead: error processing user {UserId}", userId);
            }
        }
    }

    private async Task ProcessUserAsync(Guid userId, DateOnly today, DateOnly horizon, CancellationToken ct)
    {
        var tickers = await universe.EarningsTickersAsync(userId, ct);
        if (tickers.Count == 0)
        {
            return;
        }

        IReadOnlyList<EarningsEvent> events;
        try
        {
            events = await earningsCalendar.GetForTickersAsync(tickers, today, horizon, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Yahoo quoteSummary has no contract — a failed fetch means no signal this run, not a
            // job failure. The daily cron simply tries again tomorrow.
            logger.LogWarning(ex, "EarningsAhead: earnings-calendar fetch failed for user {UserId}", userId);
            return;
        }

        foreach (var evt in events)
        {
            await RaiseAsync(userId, evt, ct);
        }

        IReadOnlyList<EarningsEvent> pastEvents;
        try
        {
            pastEvents = await earningsCalendar.GetForTickersAsync(
                tickers, today.AddDays(-ResolveLookbackDays), today.AddDays(-1), null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "EarningsAhead: earnings-calendar resolve fetch failed for user {UserId}", userId);
            return;
        }

        foreach (var evt in pastEvents)
        {
            await ResolveAsync(userId, evt, ct);
        }
    }

    private Task RaiseAsync(Guid userId, EarningsEvent evt, CancellationToken ct)
    {
        var alertEventType = MapEventType(evt.EventType);
        if (alertEventType is null)
        {
            return Task.CompletedTask;
        }

        return alerts.GenerateEarningsAheadAlertAsync(
            userId, evt.Ticker, alertEventType, evt.EventDate, evt.IsEstimate, ct);
    }

    private Task ResolveAsync(Guid userId, EarningsEvent evt, CancellationToken ct)
    {
        var alertEventType = MapEventType(evt.EventType);
        if (alertEventType is null)
        {
            return Task.CompletedTask;
        }

        return alerts.ResolveEarningsAheadAlertAsync(userId, evt.Ticker, alertEventType, evt.EventDate, ct);
    }

    private static string? MapEventType(string eventType) => eventType switch
    {
        EarningsEventType.Earnings => EarningsAheadEventType.Earnings,
        EarningsEventType.ExDividend => EarningsAheadEventType.ExDividend,
        // Dividend-payment dates (as opposed to ex-dividend) are not part of this signal.
        _ => null,
    };
}
