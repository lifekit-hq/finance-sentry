namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public enum CoverageHoleRepairStatus
{
    /// <summary>The hole starts later than its account's first quiet week: apply moves it back.</summary>
    Pending,

    /// <summary>The hole already starts at its first quiet week (an earlier apply, or it never started late).</summary>
    Unchanged,
}

/// <summary>One open hole, old start against new. <see cref="NewFrom"/> equals <see cref="OldFrom"/> unless the row is pending.</summary>
public sealed record CoverageHoleRepairRow(
    Guid HoleId, Guid UserId, Guid AccountId, DateOnly OldFrom, DateOnly NewFrom, CoverageHoleRepairStatus Status);

public sealed record CoverageHoleRepairReport(bool Applied, IReadOnlyList<CoverageHoleRepairRow> Rows)
{
    public int Pending => Rows.Count(r => r.Status == CoverageHoleRepairStatus.Pending);

    public int Unchanged => Rows.Count(r => r.Status == CoverageHoleRepairStatus.Unchanged);
}

/// <summary>
/// Moves each open coverage hole back to the first quiet week of its gap. The sentinel used to open a hole only as far
/// back as an unbroken run of quiet weeks, so one stray purchase week inside a long gap left the weeks before it outside
/// the hole (see <see cref="CoverageGapDetector.Detect"/>); the rule is fixed, this repairs the holes already recorded.
/// Only an open hole is touched, and only its <c>From</c>, and only earlier: a closed hole is history, and a start never
/// moves later.
/// <para>
/// A dry run (the default) reads and reports and writes nothing, not even the backup table. Apply, in one transaction:
/// creates <c>bank_sync.coverage_holes_backup_start</c> if missing, copies every pending hole's original row in (a hole
/// whose backup already exists is never copied again, so the backup always holds the originals), then moves the start. A
/// second apply finds every hole at its first quiet week and changes nothing. Reads and writes carry no principal: the
/// verb runs outside a request, so the queries opt out of the Owner filter.
/// </para>
/// </summary>
public sealed class CoverageHoleStartRepairService(BankSyncDbContext db)
{
    public const string BackupTable = "coverage_holes_backup_start";

    public async Task<CoverageHoleRepairReport> RepairAsync(Guid? accountId, bool apply, CancellationToken ct = default)
    {
        var holes = await db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(h => h.To == null && (accountId == null || h.AccountId == accountId))
            .OrderBy(h => h.From)
            .ToListAsync(ct);

        // A materialised array: EF Core translates Contains over it, not over the IReadOnlySet the detector publishes.
        var dailyCategories = CoverageGapDetector.DailyCategories.ToArray();
        var rows = new List<CoverageHoleRepairRow>(holes.Count);

        foreach (var hole in holes)
        {
            var currency = await db.BankAccounts
                .IgnoreQueryFilters([OwnerQueryFilter.Name, BankSyncDbContext.AccountActiveFilterName])
                .AsNoTracking()
                .Where(a => a.Id == hole.AccountId)
                .Select(a => a.Currency)
                .FirstOrDefaultAsync(ct);
            if (currency is null)
            {
                rows.Add(new CoverageHoleRepairRow(hole.Id, hole.UserId, hole.AccountId, hole.From, hole.From, CoverageHoleRepairStatus.Unchanged));
                continue;
            }

            var spend = db.Transactions.IgnoreQueryFilters([OwnerQueryFilter.Name])
                .AsNoTracking()
                .Where(t => t.AccountId == hole.AccountId
                         && t.IsActive
                         && !t.IsPending
                         && t.TransactionType == "debit"
                         && t.MerchantCategory != null
                         && dailyCategories.Contains(t.MerchantCategory));

            var first = await spend.OrderBy(t => t.TransactionDate).Select(t => (DateTime?)t.TransactionDate).FirstOrDefaultAsync(ct);
            if (first is null)
            {
                rows.Add(new CoverageHoleRepairRow(hole.Id, hole.UserId, hole.AccountId, hole.From, hole.From, CoverageHoleRepairStatus.Unchanged));
                continue;
            }

            var lowest = hole.From.AddDays(-7 * CoverageGapDetector.MaxQuietWeeks).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var debits = (await spend.Where(t => t.TransactionDate >= lowest)
                    .Select(t => new { t.TransactionDate, t.Amount })
                    .ToListAsync(ct))
                .Select(t => (Date: t.TransactionDate, Amount: t.Amount));

            var weekly = CoverageGapDetector.WeeklySpendUsd(currency, debits);
            var firstWeek = CoverageGapDetector.WeekStart(DateOnly.FromDateTime(first.Value));
            var start = CoverageGapDetector.FirstQuietWeek(weekly, firstWeek, hole.From, hole.BaselineWeeklySpendUsd);

            rows.Add(new CoverageHoleRepairRow(
                hole.Id, hole.UserId, hole.AccountId, hole.From, start,
                start < hole.From ? CoverageHoleRepairStatus.Pending : CoverageHoleRepairStatus.Unchanged));
        }

        var pending = rows.Where(r => r.Status == CoverageHoleRepairStatus.Pending).ToList();
        var report = new CoverageHoleRepairReport(apply && pending.Count > 0, rows);
        if (!apply || pending.Count == 0)
            return report;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await BackUpAsync([.. pending.Select(r => r.HoleId)], ct);
        foreach (var row in pending)
        {
            // Guarded on the row still being open and still starting later, so a concurrent close or a repeat is a no-op.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE bank_sync.coverage_holes SET "From" = {row.NewFrom}, "UpdatedAt" = now()
                WHERE "Id" = {row.HoleId} AND "To" IS NULL AND "From" > {row.NewFrom}
                """, ct);
        }

        await transaction.CommitAsync(ct);
        return report;
    }

    // The backup takes the live table's shape (LIKE) plus when the copy was made. Rows are copied only while absent from
    // the backup, so it keeps the originals however often the repair runs.
    private async Task BackUpAsync(Guid[] holeIds, CancellationToken ct)
    {
        const string backup = "bank_sync." + BackupTable;

        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS " + backup
            + " (LIKE bank_sync.coverage_holes INCLUDING DEFAULTS, backed_up_at timestamp with time zone NOT NULL DEFAULT now());",
            ct);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO " + backup + " SELECT h.*, now() FROM bank_sync.coverage_holes h"
            + " WHERE h.\"Id\" = ANY({0})"
            + " AND NOT EXISTS (SELECT 1 FROM " + backup + " b WHERE b.\"Id\" = h.\"Id\")",
            [holeIds], ct);
    }
}
