namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Daily sentinel (044/US3): fires a CategorySpike alert when month-to-date spend in a category
/// exceeds the 6-month baseline by more than the configured multiplier. Supersedes the retired
/// UnusualSpend sentinel: same currency-aware USD conversion, but a 6-month lookback, a configurable
/// threshold, and a debit predicate that matches how adapters actually store direction.
/// </summary>
public sealed class CategorySpikeDetectionJob(
    BankSyncDbContext db,
    IAlertGeneratorService alerts,
    IOptions<HygieneSentinelsOptions> options,
    ILogger<CategorySpikeDetectionJob> logger)
{
    private const int BaselineMonths = 6;

    // A category has to be established before a spike over its norm means anything.
    private const int MinHistoryMonths = 4;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var multiplier = options.Value.CategorySpikeMultiplier;
        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var historyStart = currentMonthStart.AddMonths(-BaselineMonths);

        IReadOnlyList<SpendRow> rows;
        ActiveAccountSnapshot accounts;
        try
        {
            accounts = await ActiveAccountSnapshot.ReadAsync(db, ct);
            var activeAccountIds = accounts.AccountIds;

            // Debit-only and posted-only — the same predicate DuplicateChargeDetectionJob uses.
            // Direction lives in TransactionType, not the sign: every persist path runs
            // `Transaction.ValidateInvariants`, which rejects a negative Amount, so the sign test is
            // a defensive arm only. A refund (credit) must never inflate a category's spend, and a
            // pending charge coexists with its posted twin (they hash differently) — counting both
            // would double the month's spend.
            rows = await db.Transactions
                .AsNoTracking()
                .Where(t => t.MerchantCategory != null
                         && (t.Amount < 0 || t.TransactionType == "debit")
                         && t.TransactionDate >= historyStart
                         && t.IsActive
                         && !t.IsPending
                         && activeAccountIds.Contains(t.AccountId))
                .Select(t => new SpendRow(t.UserId, t.AccountId, t.MerchantCategory!, t.TransactionDate, t.Amount))
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CategorySpikeDetectionJob: failed to read transactions");
            return;
        }

        decimal ToUsd(Guid accountId, decimal amount) =>
            CurrencyConverter.ToUsd(Math.Abs(amount), accounts.CurrencyOf(accountId));

        // How many of the BaselineMonths this user was actually visible for. A month holding no
        // charge in one category is a real zero and belongs in that category's denominator, but a
        // month before the user's first transaction is *no data* — counting it as a zero would
        // deflate every category at once and greet a newly connected account with an alert burst.
        var observedMonthsByUser = rows
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => MonthsBetween(FirstOfMonth(g.Min(r => r.Date)), currentMonthStart));

        var grouped = rows.GroupBy(r => new { r.UserId, r.Category });

        foreach (var group in grouped)
        {
            var byMonth = group
                .GroupBy(r => new { r.Date.Year, r.Date.Month })
                .ToDictionary(g => g.Key, g => g.Sum(x => ToUsd(x.AccountId, x.Amount)));

            var historicMonths = byMonth
                .Where(kv => new DateTime(kv.Key.Year, kv.Key.Month, 1) < currentMonthStart)
                .ToList();

            if (historicMonths.Count < MinHistoryMonths) continue;

            var currentKey = new { currentMonthStart.Year, currentMonthStart.Month };
            if (!byMonth.TryGetValue(currentKey, out var currentMonth) || currentMonth <= 0) continue;

            // Average monthly spend over the months observed, not over the months that happen to
            // hold rows for this category. Dividing by the latter made the sentinel quietly weakest
            // exactly where a spike is most visible: a category billed in 3 of 6 months carried a
            // baseline twice its true monthly average, so the multiplier had to be cleared against
            // a number no month ever spent.
            var baseline = historicMonths.Sum(kv => kv.Value) / observedMonthsByUser[group.Key.UserId];
            if (baseline <= 0) continue;

            if (currentMonth <= baseline * multiplier) continue;

            try
            {
                await alerts.GenerateCategorySpikeAlertAsync(
                    group.Key.UserId, group.Key.Category, currentMonth, baseline, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "CategorySpikeDetectionJob: alert failed for user {UserId} category {Category}",
                    group.Key.UserId, group.Key.Category);
            }
        }
    }

    private static DateTime FirstOfMonth(DateTime date) =>
        new(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Complete months from <paramref name="from"/> up to (not including) <paramref name="to"/>,
    /// clamped into 1..BaselineMonths. The query window already bounds this to BaselineMonths; the
    /// floor of 1 keeps a first-month user from dividing by zero.
    /// </summary>
    private static int MonthsBetween(DateTime from, DateTime to) =>
        Math.Clamp(((to.Year - from.Year) * 12) + to.Month - from.Month, 1, BaselineMonths);

    private sealed record SpendRow(Guid UserId, Guid AccountId, string Category, DateTime Date, decimal Amount);
}
