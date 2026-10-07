namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily Hangfire job (pick 6 of #825, notifications A): turns the street's upgrades and downgrades
/// (<see cref="AnalystAction"/>) into at most one <see cref="IAlertGeneratorService.GenerateAnalystRatingChangeAlertAsync"/>
/// alert per ticker per action day, summarising every firm that moved that day. Covers a user's held
/// equities always, and their watchlist only when the profile's <c>WatchlistAnalystAlerts</c> toggle is
/// on (<see cref="IUserAlertPreferencesReader"/>). Runs after the 01:00 UTC ingestion and looks back
/// <see cref="LookbackDays"/> days rather than only at today, so an action dated the previous US session
/// (ingested overnight) or a run lost to a deploy still alerts; the generator dedups once per
/// (ticker, day), which makes the overlap a no-op. A name that is both held and watched alerts once.
/// Initiations, target changes and reiterations are not rating changes and never alert.
/// </summary>
public sealed class AnalystRatingChangeAlertJob(
    IBankingTotalsReader banking,
    LookaheadUniverse universe,
    IAnalystActionRepository actions,
    IUserAlertPreferencesReader preferences,
    IAlertGeneratorService alerts,
    ILogger<AnalystRatingChangeAlertJob> logger)
{
    /// <summary>The Hangfire recurring-job id this detector is scheduled under.</summary>
    public const string RecurringJobId = "analyst-rating-change-alerts";

    /// <summary>How many days back from today an action date still counts as a fresh rating change.</summary>
    private const int LookbackDays = 2;

    public Task ExecuteAsync(CancellationToken ct = default) => ExecuteAsync(DateTime.UtcNow, ct);

    /// <summary>Overload taking the reference instant explicitly, so tests aren't at the mercy of the day they run on.</summary>
    public async Task ExecuteAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var userIds = await banking.GetActiveUserIdsAsync(ct);
        if (userIds.Count == 0)
        {
            logger.LogDebug("AnalystRatingChange: no active users found, skipping.");
            return;
        }

        var since = DateOnly.FromDateTime(nowUtc).AddDays(-LookbackDays);

        foreach (var userId in userIds)
        {
            try
            {
                await ProcessUserAsync(userId, since, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "AnalystRatingChange: error processing user {UserId}", userId);
            }
        }
    }

    private async Task ProcessUserAsync(Guid userId, DateOnly since, CancellationToken ct)
    {
        var tickers = new HashSet<string>(await universe.HeldEquityTickersAsync(userId, ct), StringComparer.OrdinalIgnoreCase);

        if ((await preferences.GetAsync(userId, ct))?.WatchlistAnalystAlerts == true)
        {
            tickers.UnionWith(await universe.WatchlistTickersAsync(userId, ct));
        }

        if (tickers.Count == 0)
        {
            return;
        }

        var changes = await actions.ListRatingChangesAsync(tickers, since, ct);

        foreach (var day in changes.GroupBy(a => (a.Ticker, a.ActionDate)))
        {
            var summary = day
                .Select(a => new AnalystRatingChange(
                    a.Firm, a.ActionType == AnalystActionType.Upgrade, a.PriorRating, a.NewRating))
                .ToList();

            await alerts.GenerateAnalystRatingChangeAlertAsync(userId, day.Key.Ticker, day.Key.ActionDate, summary, ct);
        }
    }
}
