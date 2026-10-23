namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily coverage sentinel: an account whose day-to-day spend used to flow and has gone quiet (see
/// <see cref="CoverageGapDetector"/>) while its syncs succeed is most likely not where the person pays any more. It
/// raises a <c>CoverageGap</c> alert and records the period as a <see cref="CoverageHole"/> that budget and estimate
/// readers skip (<see cref="ICoverageHoleReader"/>). The hole closes, and the alert resolves, when the account's spend
/// flows again. Only reads transactions and sync history we already hold: no provider call.
/// </summary>
public sealed class CoverageSentinelJob(
    BankSyncDbContext db,
    IClosedAccountsReader closedAccounts,
    IAlertGeneratorService alerts,
    TimeProvider time,
    ILogger<CoverageSentinelJob> logger)
{
    /// <summary>The newest successful sync an account needs for its quiet to count: a failing sync is a different alert.</summary>
    public const int SyncRecencyDays = 3;

    /// <summary>
    /// Weeks of spend read: the longest quiet run plus the baseline before it, with one spare week so a run that starts at
    /// the window's edge is still seen whole.
    /// </summary>
    private const int ReadWeeks = CoverageGapDetector.MaxQuietWeeks + CoverageGapDetector.BaselineWeeks + 1;

    [Hangfire.AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var latestWeek = CoverageGapDetector.LatestCompleteWeek(DateOnly.FromDateTime(now.UtcDateTime));
        var readFrom = latestWeek.AddDays(-7 * ReadWeeks).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var closedUsers = (await closedAccounts.GetClosedUserIdsAsync(ct)).ToList();

        var accounts = await db.BankAccounts.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(a => a.ClosedAt == null && !closedUsers.Contains(a.UserId))
            .Select(a => new AccountRow(a.Id, a.UserId, a.Currency, a.BankName, a.AccountNumberLast4))
            .ToListAsync(ct);
        var accountIds = accounts.Select(a => a.Id).ToList();

        // A materialised array: EF Core translates Contains over it, not over the IReadOnlySet the detector publishes.
        var dailyCategories = CoverageGapDetector.DailyCategories.ToArray();

        var rows = await db.Transactions.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(t => t.IsActive
                     && !t.IsPending
                     && t.TransactionType == "debit"
                     && t.MerchantCategory != null
                     && dailyCategories.Contains(t.MerchantCategory)
                     && t.TransactionDate >= readFrom
                     && accountIds.Contains(t.AccountId))
            .Select(t => new SpendRow(t.AccountId, t.TransactionDate, t.Amount))
            .ToListAsync(ct);

        var syncedSince = now.UtcDateTime.AddDays(-SyncRecencyDays);
        var recentlySynced = (await db.SyncJobs.IgnoreQueryFilters([OwnerQueryFilter.Name])
                .AsNoTracking()
                .Where(j => j.Status == "success" && j.CompletedAt >= syncedSince && accountIds.Contains(j.AccountId))
                .Select(j => j.AccountId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var openHoles = await db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(h => h.To == null)
            .ToDictionaryAsync(h => h.AccountId, ct);

        var spendByAccount = rows
            .GroupBy(r => r.AccountId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var account in accounts)
        {
            openHoles.TryGetValue(account.Id, out var openHole);
            if (!spendByAccount.TryGetValue(account.Id, out var accountRows) && openHole is null)
                continue;

            try
            {
                var weekly = Weekly(account.Currency, accountRows ?? []);
                var synced = recentlySynced.Contains(account.Id);

                if (openHole is not null)
                    await ReviewOpenHoleAsync(account, openHole, weekly, latestWeek, synced, ct);
                else if (synced)
                    await DetectAsync(account, weekly, latestWeek, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "CoverageSentinelJob: failed for account {AccountId}", account.Id);
                db.ChangeTracker.Clear();
            }
        }

        var watched = accountIds.ToHashSet();
        foreach (var hole in openHoles.Values.Where(h => !watched.Contains(h.AccountId)))
        {
            try
            {
                await CloseUnwatchedHoleAsync(hole, latestWeek, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "CoverageSentinelJob: failed to close the hole of unwatched account {AccountId}", hole.AccountId);
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// An account that is closed, or whose person is, is no longer watched, so its open hole would never close: it ends
    /// with the latest complete week and its alert resolves.
    /// </summary>
    private async Task CloseUnwatchedHoleAsync(CoverageHole hole, DateOnly latestWeek, CancellationToken ct)
    {
        await alerts.ResolveCoverageGapAlertAsync(hole.UserId, hole.AccountId, ct);
        await CloseHoleAsync(hole.Id, latestWeek.AddDays(6), ct);

        logger.LogInformation("Coverage gap on account {AccountId} closed: the account is no longer watched.", hole.AccountId);
    }

    private async Task DetectAsync(
        AccountRow account, Dictionary<DateOnly, decimal> weekly, DateOnly latestWeek, CancellationToken ct)
    {
        if (weekly.Count == 0)
            return;

        var finding = CoverageGapDetector.Detect(weekly, weekly.Keys.Min(), latestWeek);
        if (finding is null)
            return;

        // Alert first: when it fails nothing is stored, so tomorrow's run finds the gap again instead of leaving a hole
        // that never told anyone. The alert's own dedup absorbs the repeat when only the save fails.
        await alerts.GenerateCoverageGapAlertAsync(
            account.UserId, account.Id, account.DisplayName, finding.QuietWeeks, finding.BaselineWeeklySpendUsd, ct);

        db.CoverageHoles.Add(new CoverageHole
        {
            UserId = account.UserId,
            AccountId = account.Id,
            From = finding.QuietFrom,
            BaselineWeeklySpendUsd = finding.BaselineWeeklySpendUsd,
        });
        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Coverage gap on account {AccountId}: daily spend quiet for {Weeks} weeks from {From} (usual ${Baseline:F0} a week).",
            account.Id, finding.QuietWeeks, finding.QuietFrom, finding.BaselineWeeklySpendUsd);
    }

    private async Task ReviewOpenHoleAsync(
        AccountRow account, CoverageHole hole, Dictionary<DateOnly, decimal> weekly, DateOnly latestWeek, bool synced,
        CancellationToken ct)
    {
        // Closing mirrors opening: the hole stays open until MinQuietWeeks consecutive complete weeks are no longer
        // quiet, so one stray purchase in a quiet stretch does not end it. A sync that is not succeeding keeps it open
        // too: with no fresh read the account's weeks say nothing about whether the spend came back.
        var recoveredFrom = latestWeek.AddDays(-7 * (CoverageGapDetector.MinQuietWeeks - 1));
        if (!synced || recoveredFrom <= hole.From
            || Enumerable.Range(0, CoverageGapDetector.MinQuietWeeks).Any(i => CoverageGapDetector.IsQuiet(
                CoverageGapDetector.Spend(weekly, recoveredFrom.AddDays(7 * i)), hole.BaselineWeeklySpendUsd)))
            return;

        // Resolve first (idempotent): when it fails the hole stays open and tomorrow's run retries the resolve.
        await alerts.ResolveCoverageGapAlertAsync(account.UserId, account.Id, ct);
        await CloseHoleAsync(hole.Id, recoveredFrom.AddDays(-1), ct);

        logger.LogInformation("Coverage gap on account {AccountId} closed: daily spend flows again from {Week}.", account.Id, recoveredFrom);
    }

    /// <summary>
    /// Closes the hole read fresh by id: the open holes are loaded untracked, so another account's failure, which clears
    /// the change tracker, cannot drop this write.
    /// </summary>
    private async Task CloseHoleAsync(Guid holeId, DateOnly to, CancellationToken ct)
    {
        var hole = await db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).SingleAsync(h => h.Id == holeId, ct);
        hole.To = to;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Weekly spend in USD by week-start Monday; converted once here, where the account's currency is in scope.</summary>
    private static Dictionary<DateOnly, decimal> Weekly(string currency, List<SpendRow> rows)
        => rows
            .GroupBy(r => CoverageGapDetector.WeekStart(DateOnly.FromDateTime(r.Date)))
            .ToDictionary(g => g.Key, g => g.Sum(r => CurrencyConverter.ToUsd(Math.Abs(r.Amount), currency)));

    private sealed record SpendRow(Guid AccountId, DateTime Date, decimal Amount);

    private sealed record AccountRow(Guid Id, Guid UserId, string Currency, string BankName, string Last4)
    {
        public string DisplayName => $"{BankName} ···{Last4}";
    }
}
