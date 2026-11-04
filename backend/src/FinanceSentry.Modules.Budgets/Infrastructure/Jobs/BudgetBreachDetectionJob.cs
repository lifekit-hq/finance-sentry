namespace FinanceSentry.Modules.Budgets.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Budgets.API.Responses;
using FinanceSentry.Modules.Budgets.Application.Services;
using FinanceSentry.Modules.Budgets.Domain;
using FinanceSentry.Modules.Budgets.Domain.Repositories;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily hygiene sentinel (C2, ledger-heartbeat design). The amounts it watches are the hand-set ones: a line the
/// person never set an amount for carries no alert (an alert on a number they never chose is noise), so the sentinel
/// walks the override rows (<see cref="IBudgetRepository"/>) and reads each user's month through
/// <see cref="IBudgetEstimateService"/>, the same figures the page shows. For every overridden line it fires a
/// BudgetBreach alert when month-to-date spend reaches 90% or 100% of the amount, or when its pace
/// (<see cref="BudgetPace"/>) projects an overshoot by day 7 through day 21 of the month. For a person who has
/// hand-set any amount it also fires one alert on the month as a whole when its expected month-end total beats the
/// total estimate by <see cref="BudgetPace.Tolerance"/> in the same days (only where there is history enough for the
/// total to mean something). Spend is already USD at the read boundary; an override's own amount is converted by the
/// estimate service, so the two sides are compared apples-to-apples.
///
/// Both thresholds are checked independently every run, not as an if/else-if ladder: a line that jumps straight from
/// 70% to 105% spend in one day crosses both the same day, and each is its own alert. Dedup — the part that makes a
/// crossing fire once per line per month — lives entirely in <see cref="IAlertGeneratorService"/> (its reference id
/// carries the line, the crossing kind, and the year/month); this job just reports what it observed on every run.
///
/// A run evaluates only the UTC month of the daily slot it was scheduled for
/// (<see cref="ScheduledSlotUtc"/>), not of the moment it happens to execute: the slot is late in
/// the UTC day so the last run covering a month happens after that month's final day of spending
/// has synced, and a last-day run that starts a few minutes after midnight still covers that month.
/// A run starting more than <see cref="MaxStartDelay"/> past its slot is skipped.
///
/// Two decisions worth stating (each pinned by its own test):
/// - A mid-month amount edit is never snapshotted — every run reads the CURRENT override and recomputes against it.
///   An edit changes what counts as a crossing going forward; it never retroactively resolves an alert already
///   raised earlier in the month.
/// - A refund that drops spend back under an already-alerted threshold does not resolve that
///   alert — BudgetBreach has no Resolve method, like the other hygiene sentinels (PriceHike,
///   CategorySpike, …). If spend later climbs back over the same threshold in the same month, the
///   alert already raised on that (line, kind, month) reference — open, dismissed or resolved —
///   suppresses a second one.
/// </summary>
public sealed class BudgetBreachDetectionJob(
    IBudgetRepository budgets,
    IBudgetEstimateService estimates,
    IAlertGeneratorService alerts,
    TimeProvider clock,
    ILogger<BudgetBreachDetectionJob> logger)
{
    private const decimal NearLimitThreshold = 0.90m;
    private const decimal ExceededThreshold = 1.0m;

    /// <summary>
    /// Pace fires only mid-month: before day 7 the elapsed-fraction denominator is small enough
    /// that one large charge invents a crisis, and after day 21 the exceeded/near-limit thresholds
    /// are the better signal — a pace alert that late would be a second notification about the
    /// same thing.
    /// </summary>
    private const int PaceWindowStartDay = BudgetPace.WindowStartDay;
    private const int PaceWindowEndDay = BudgetPace.WindowEndDay;

    private const decimal PaceTolerance = BudgetPace.Tolerance;

    /// <summary>The daily UTC slot this job is scheduled for (see <c>BudgetsModule</c>).</summary>
    public static readonly TimeOnly ScheduledSlotUtc = new(23, 55);

    /// <summary>
    /// How late a run may start past its slot and still evaluate the slot's month — enough for a
    /// redeploy or a queue backlog. A run starting later than this is skipped, not caught up.
    /// </summary>
    public static readonly TimeSpan MaxStartDelay = TimeSpan.FromHours(2);

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var slot = DateOnly.FromDateTime(now).ToDateTime(ScheduledSlotUtc, DateTimeKind.Utc);
        if (slot > now)
        {
            slot = slot.AddDays(-1);
        }

        if (now - slot > MaxStartDelay)
        {
            logger.LogWarning(
                "BudgetBreachDetectionJob: started {Delay} after its {Slot:O} slot — skipped", now - slot, slot);
            return;
        }

        IReadOnlyList<Budget> all;
        try
        {
            all = await budgets.GetAllUnscopedAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BudgetBreachDetectionJob: failed to read budgets");
            return;
        }

        foreach (var group in all.GroupBy(b => b.UserId))
        {
            await ProcessUserAsync(group.Key, group.ToList(), slot, ct);
        }
    }

    private async Task ProcessUserAsync(
        Guid userId, IReadOnlyList<Budget> overrides, DateTime slot, CancellationToken ct)
    {
        // A breach that only becomes visible after the month closes, because a bank posted late, is not alerted.
        BudgetSummaryResponse summary;
        try
        {
            summary = await estimates.ComputeAsync(userId, slot.Year, slot.Month, slot, overrides, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BudgetBreachDetectionJob: failed to read the month for user {UserId}", userId);
            return;
        }

        foreach (var item in summary.Items.Where(i => i.OverrideId is not null))
        {
            try
            {
                await EvaluateLineAsync(userId, item, slot, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "BudgetBreachDetectionJob: alert failed for user {UserId} category {Category}",
                    userId, item.Category);
            }
        }

        try
        {
            await EvaluateMonthAsync(userId, summary, slot, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BudgetBreachDetectionJob: month alert failed for user {UserId}", userId);
        }
    }

    private async Task EvaluateLineAsync(Guid userId, BudgetSummaryItemDto item, DateTime asOf, CancellationToken ct)
    {
        var limitUsd = item.Estimate ?? 0m;
        if (limitUsd <= 0m || item.OverrideId is not { } budgetId)
        {
            return;
        }

        var year = asOf.Year;
        var month = asOf.Month;
        var spentUsd = item.Spent;
        var ratio = spentUsd / limitUsd;

        if (ratio >= NearLimitThreshold)
        {
            await alerts.GenerateBudgetNearLimitAlertAsync(
                userId, budgetId, item.Category, spentUsd, limitUsd, year, month, ct);
        }

        if (ratio >= ExceededThreshold)
        {
            await alerts.GenerateBudgetExceededAlertAsync(
                userId, budgetId, item.Category, spentUsd, limitUsd, year, month, ct);
        }

        if (asOf.Day >= PaceWindowStartDay && asOf.Day <= PaceWindowEndDay)
        {
            var pace = BudgetPace.Calculate(spentUsd, limitUsd, asOf);
            if (pace.PaceRatio >= PaceTolerance)
            {
                await alerts.GenerateBudgetPaceAlertAsync(
                    userId, budgetId, item.Category, spentUsd, limitUsd,
                    pace.ProjectedMonthEndSpend, year, month, ct);
            }
        }
    }

    private async Task EvaluateMonthAsync(Guid userId, BudgetSummaryResponse summary, DateTime asOf, CancellationToken ct)
    {
        // Without enough history the total is only the hand-set amounts, and the spend of every other line would
        // read as an overshoot of a total that never included it.
        if (!summary.HasEnoughHistory
            || summary.TotalEstimate <= 0m
            || asOf.Day < PaceWindowStartDay
            || asOf.Day > PaceWindowEndDay)
        {
            return;
        }

        if (summary.ExpectedMonthEnd / summary.TotalEstimate >= PaceTolerance)
        {
            await alerts.GenerateBudgetTotalPaceAlertAsync(
                userId, summary.TotalEstimate, summary.ExpectedMonthEnd, summary.TotalSpent,
                asOf.Year, asOf.Month, ct);
        }
    }
}
