namespace FinanceSentry.Modules.Budgets.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Budgets.Application.Services;
using FinanceSentry.Modules.Budgets.Domain;
using FinanceSentry.Modules.Budgets.Domain.Repositories;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily hygiene sentinel (C2, ledger-heartbeat design): fires a BudgetBreach alert when a monthly
/// budget's month-to-date spend reaches 90% or 100% of its limit. Reads budgets.budgets
/// (<see cref="IBudgetRepository"/>) and month-to-date category spend, already converted to USD at
/// the read boundary (<see cref="IMerchantSpendingReader"/> — never a native amount is summed). The
/// budget's own limit is converted the same way via <see cref="CurrencyConverter.ToUsd"/>, mirroring
/// <c>GetBudgetSummaryQueryHandler</c> so the two figures are compared apples-to-apples.
///
/// Both thresholds are checked independently every run, not as an if/else-if ladder: a budget that
/// jumps straight from 70% to 105% spend in one day crosses both the same day, and each is its own
/// alert. Dedup — the part that makes a crossing fire once per budget per month — lives entirely in
/// <see cref="IAlertGeneratorService"/> (its reference id carries the budget, the crossing kind, and
/// the year/month); this job just reports what it observed on every run.
///
/// A run evaluates only the UTC month of the daily slot it was scheduled for
/// (<see cref="ScheduledSlotUtc"/>), not of the moment it happens to execute: the slot is late in
/// the UTC day so the last run covering a month happens after that month's final day of spending
/// has synced, and a last-day run that starts a few minutes after midnight still covers that month.
/// A run starting more than <see cref="MaxStartDelay"/> past its slot is skipped.
///
/// Two decisions worth stating (each pinned by its own test):
/// - A mid-month limit edit is never snapshotted — every run reads the budget's CURRENT
///   MonthlyLimit and recomputes against it. An edit changes what counts as a crossing going
///   forward; it never retroactively resolves an alert already raised earlier in the month.
/// - A refund that drops spend back under an already-alerted threshold does not resolve that
///   alert — BudgetBreach has no Resolve method, like the other hygiene sentinels (PriceHike,
///   CategorySpike, …). If spend later climbs back over the same threshold in the same month, the
///   alert already raised on that (budget, kind, month) reference — open, dismissed or resolved —
///   suppresses a second one.
/// </summary>
public sealed class BudgetBreachDetectionJob(
    IBudgetRepository budgets,
    IMerchantSpendingReader merchantSpending,
    ICategoryNormalizationService normalization,
    IAlertGeneratorService alerts,
    TimeProvider clock,
    ILogger<BudgetBreachDetectionJob> logger)
{
    private const decimal NearLimitThreshold = 0.90m;
    private const decimal ExceededThreshold = 1.0m;

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

        // A breach that only becomes visible after the month closes, because a bank posted late, is not alerted.
        var monthStart = new DateOnly(slot.Year, slot.Month, 1);

        IReadOnlyList<Budget> all;
        try
        {
            all = await budgets.GetAllAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BudgetBreachDetectionJob: failed to read budgets");
            return;
        }

        foreach (var group in all.GroupBy(b => b.UserId))
        {
            await ProcessUserAsync(group.Key, group.ToList(), monthStart, ct);
        }
    }

    private async Task ProcessUserAsync(
        Guid userId, IReadOnlyList<Budget> userBudgets, DateOnly monthStart, CancellationToken ct)
    {
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        IReadOnlyDictionary<string, decimal> rawSpending;
        try
        {
            rawSpending = await merchantSpending.GetSpendingByCategoryUsdAsync(userId, monthStart, monthEnd, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BudgetBreachDetectionJob: failed to read spend for user {UserId}", userId);
            return;
        }

        var spentByCategory = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawCategory, amount) in rawSpending)
        {
            var normalized = normalization.Normalize(rawCategory);
            spentByCategory[normalized] = spentByCategory.GetValueOrDefault(normalized) + amount;
        }

        foreach (var budget in userBudgets)
        {
            try
            {
                await EvaluateAsync(budget, spentByCategory, monthStart.Year, monthStart.Month, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "BudgetBreachDetectionJob: alert failed for user {UserId} category {Category}",
                    userId, budget.Category);
            }
        }
    }

    private async Task EvaluateAsync(
        Budget budget, IReadOnlyDictionary<string, decimal> spentByCategory, int year, int month,
        CancellationToken ct)
    {
        var limitUsd = CurrencyConverter.ToUsd(budget.MonthlyLimit, budget.Currency);
        if (limitUsd <= 0m)
        {
            return;
        }

        var spentUsd = spentByCategory.GetValueOrDefault(budget.Category, 0m);
        var ratio = spentUsd / limitUsd;

        if (ratio >= NearLimitThreshold)
        {
            await alerts.GenerateBudgetNearLimitAlertAsync(
                budget.UserId, budget.Id, budget.Category, spentUsd, limitUsd, year, month, ct);
        }

        if (ratio >= ExceededThreshold)
        {
            await alerts.GenerateBudgetExceededAlertAsync(
                budget.UserId, budget.Id, budget.Category, spentUsd, limitUsd, year, month, ct);
        }
    }
}
