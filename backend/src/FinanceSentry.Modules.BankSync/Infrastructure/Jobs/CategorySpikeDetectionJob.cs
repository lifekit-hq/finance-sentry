namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily sentinel (044/US3): fires a CategorySpike alert when month-to-date spend in a category
/// exceeds the 6-month baseline by more than the configured multiplier. Uses the same currency-aware
/// USD conversion as UnusualSpendDetectionJob; differs in lookback (6 vs 3 months) and configurable
/// threshold.
/// </summary>
public sealed class CategorySpikeDetectionJob(
    BankSyncDbContext db,
    IAlertGeneratorService alerts,
    IConfiguration config,
    ILogger<CategorySpikeDetectionJob> logger)
{
    private const int BaselineMonths = 6;
    // Spec requires ≥4 months before firing; still average over all BaselineMonths when available.
    private const int MinHistoryMonths = 4;
    private const decimal DefaultSpikeMultiplier = 2.0m;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var multiplier = config.GetValue("HygieneSentinels:CategorySpikeMultiplier", DefaultSpikeMultiplier);
        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var historyStart = currentMonthStart.AddMonths(-BaselineMonths);

        IReadOnlyList<SpendRow> rows;
        Dictionary<Guid, string> currencyByAccount;
        try
        {
            // Liveness policy (aligned across all 044 sentinels): only transactions on active
            // accounts participate — a disconnected account's history must not raise new alerts.
            currencyByAccount = await db.BankAccounts
                .AsNoTracking()
                .Where(a => a.IsActive)
                .Select(a => new { a.Id, a.Currency })
                .ToDictionaryAsync(a => a.Id, a => a.Currency, ct);

            var activeAccountIds = currencyByAccount.Keys.ToList();

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
            CurrencyConverter.ToUsd(Math.Abs(amount),
                currencyByAccount.TryGetValue(accountId, out var c) ? c : "USD");

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

            var baseline = historicMonths.Sum(kv => kv.Value) / historicMonths.Count;
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

    private sealed record SpendRow(Guid UserId, Guid AccountId, string Category, DateTime Date, decimal Amount);
}
