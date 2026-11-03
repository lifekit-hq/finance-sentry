namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public enum NetWorthRepairRowStatus
{
    /// <summary>The row holds the total the plan expects: it is rewritten on apply.</summary>
    Pending,

    /// <summary>The row already holds the plan's proposed total: nothing to do (an earlier apply).</summary>
    AlreadyRepaired,

    /// <summary>The owner has no snapshot for the day.</summary>
    Missing,

    /// <summary>The row holds neither the expected nor the proposed total, or the repair would take a figure below zero.</summary>
    Unexpected,
}

/// <summary>One day of the repair, old against new. The new figures are what apply writes (null for a row the repair will not touch).</summary>
public sealed record NetWorthRepairRow(
    DateOnly Date,
    NetWorthRepairRowStatus Status,
    bool BrokerageRederived,
    decimal? OldBanking,
    decimal? NewBanking,
    decimal? OldBrokerage,
    decimal? NewBrokerage,
    decimal? OldTotal,
    decimal? NewTotal,
    string? Note = null);

public sealed record NetWorthHistoryRepairReport(Guid UserId, bool Applied, IReadOnlyList<NetWorthRepairRow> Rows)
{
    public int Pending => Rows.Count(r => r.Status == NetWorthRepairRowStatus.Pending);

    public int AlreadyRepaired => Rows.Count(r => r.Status == NetWorthRepairRowStatus.AlreadyRepaired);

    /// <summary>Days the plan names that the stored history cannot take: missing, or holding an unexpected total.</summary>
    public IReadOnlyList<NetWorthRepairRow> Blockers => [.. Rows.Where(r => r.Status is NetWorthRepairRowStatus.Missing or NetWorthRepairRowStatus.Unexpected)];
}

/// <summary>Apply was asked for, but a row the plan names holds something the plan was not written for; nothing was changed.</summary>
public sealed class NetWorthHistoryRepairRefusedException(IReadOnlyList<NetWorthRepairRow> blockers)
    : InvalidOperationException(
        "Refusing to apply: " + string.Join("; ", blockers.Select(b => $"{b.Date:yyyy-MM-dd} {b.Status}{(b.Note is null ? string.Empty : $" ({b.Note})")}")))
{
    public IReadOnlyList<NetWorthRepairRow> Blockers { get; } = blockers;
}

/// <summary>
/// Runs <see cref="JulyNetWorthHistoryRepairPlan"/> against one person's snapshots. A dry run (the default) reads
/// and reports; it writes nothing, not even the backup table. Apply, in one transaction: creates the backup tables if
/// missing, copies the original rows (and their per-source rows) of every pending day in, then rewrites them. A day
/// whose backup row already exists is never backed up again, so the backup always holds the originals; a day already at its
/// proposed total is skipped, so a second apply changes nothing. Apply refuses outright when any planned day is
/// missing or holds an unexpected total. Reads and writes carry the person's <c>UserId</c> and opt out of the Owner
/// query filter (the verb runs with no principal).
/// </summary>
public sealed class NetWorthHistoryRepairService(WealthDbContext db)
{
    private const decimal Tolerance = 0.005m;

    public async Task<NetWorthHistoryRepairReport> RepairAsync(Guid userId, bool apply, CancellationToken ct = default)
    {
        var days = JulyNetWorthHistoryRepairPlan.Days;
        var from = days.Min(d => d.Date);
        var to = days.Max(d => d.Date);
        var snapshots = await db.NetWorthSnapshots
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.SnapshotDate >= from && s.SnapshotDate <= to)
            .ToDictionaryAsync(s => s.SnapshotDate, ct);

        var rows = new List<NetWorthRepairRow>(days.Count);
        var pending = new List<(NetWorthSnapshot Row, NetWorthRepairDay Day, RepairedNetWorthSnapshot Repaired)>();
        foreach (var day in days)
        {
            if (!snapshots.TryGetValue(day.Date, out var row))
            {
                rows.Add(new NetWorthRepairRow(day.Date, NetWorthRepairRowStatus.Missing, day.BrokerageRederived, null, null, null, null, null, null));
                continue;
            }

            if (Near(row.TotalNetWorth, day.ProposedTotal))
            {
                rows.Add(new NetWorthRepairRow(
                    day.Date, NetWorthRepairRowStatus.AlreadyRepaired, day.BrokerageRederived,
                    row.BankingTotal, row.BankingTotal, row.BrokerageTotal, row.BrokerageTotal, row.TotalNetWorth, row.TotalNetWorth));
                continue;
            }

            if (!Near(row.TotalNetWorth, day.ExpectedTotal))
            {
                rows.Add(new NetWorthRepairRow(
                    day.Date, NetWorthRepairRowStatus.Unexpected, day.BrokerageRederived,
                    row.BankingTotal, null, row.BrokerageTotal, null, row.TotalNetWorth, null,
                    $"holds {row.TotalNetWorth:0.00}, expected {day.ExpectedTotal:0.00} or {day.ProposedTotal:0.00}"));
                continue;
            }

            var repaired = JulyNetWorthHistoryRepairPlan.Repair(row, day);
            if (repaired.Brokerage < 0m || repaired.BrokerageInvested < 0m)
            {
                rows.Add(new NetWorthRepairRow(
                    day.Date, NetWorthRepairRowStatus.Unexpected, day.BrokerageRederived,
                    row.BankingTotal, null, row.BrokerageTotal, null, row.TotalNetWorth, null,
                    "the repair would take a sleeve or its invested part below zero"));
                continue;
            }

            pending.Add((row, day, repaired));
            rows.Add(new NetWorthRepairRow(
                day.Date, NetWorthRepairRowStatus.Pending, day.BrokerageRederived,
                row.BankingTotal, repaired.Banking, row.BrokerageTotal, repaired.Brokerage, row.TotalNetWorth, repaired.Total));
        }

        var report = new NetWorthHistoryRepairReport(userId, apply && pending.Count > 0, rows);
        if (!apply)
            return report;

        if (report.Blockers.Count > 0)
            throw new NetWorthHistoryRepairRefusedException(report.Blockers);

        if (pending.Count == 0)
            return report;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await BackUpAsync(userId, [.. pending.Select(p => p.Row.Id)], ct);
        foreach (var (row, day, repaired) in pending)
            await RewriteAsync(userId, row, day, repaired, ct);

        await transaction.CommitAsync(ct);
        return report;
    }

    private static bool Near(decimal a, decimal b) => Math.Abs(a - b) <= Tolerance;

    // The backup tables take the live tables' shape (LIKE) plus when the copy was made. Rows are copied only while
    // absent from the backup, so the backup keeps the originals however often the repair runs.
    private async Task BackUpAsync(Guid userId, Guid[] snapshotIds, CancellationToken ct)
    {
        const string snapshots = "public." + JulyNetWorthHistoryRepairPlan.BackupTable;
        const string sources = "public." + JulyNetWorthHistoryRepairPlan.SourcesBackupTable;

        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS " + snapshots
            + " (LIKE public.net_worth_snapshots INCLUDING DEFAULTS, backed_up_at timestamp with time zone NOT NULL DEFAULT now());"
            + "CREATE TABLE IF NOT EXISTS " + sources
            + " (LIKE public.net_worth_snapshot_sources INCLUDING DEFAULTS, backed_up_at timestamp with time zone NOT NULL DEFAULT now());",
            ct);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO " + snapshots + " SELECT s.*, now() FROM public.net_worth_snapshots s"
            + " WHERE s.\"UserId\" = {0} AND s.\"Id\" = ANY({1})"
            + " AND NOT EXISTS (SELECT 1 FROM " + snapshots + " b WHERE b.\"Id\" = s.\"Id\")",
            [userId, snapshotIds], ct);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO " + sources + " SELECT x.*, now() FROM public.net_worth_snapshot_sources x"
            + " WHERE x.snapshot_id = ANY({0})"
            + " AND NOT EXISTS (SELECT 1 FROM " + sources + " b WHERE b.\"Id\" = x.\"Id\")",
            [snapshotIds], ct);
    }

    private async Task RewriteAsync(
        Guid userId, NetWorthSnapshot row, NetWorthRepairDay day, RepairedNetWorthSnapshot repaired, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE public.net_worth_snapshots SET
                "BankingTotal" = {repaired.Banking}, "BrokerageTotal" = {repaired.Brokerage}, "TotalNetWorth" = {repaired.Total},
                cash_total = {repaired.Cash}, brokerage_invested = {repaired.BrokerageInvested},
                stale_sleeves = {repaired.StaleSleeves}, is_approximate = {repaired.IsApproximate}, fx_status = {(int?)repaired.Fx}
            WHERE "Id" = {row.Id} AND "UserId" = {userId}
            """, ct);

        var sources = await db.NetWorthSnapshotSources
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(x => x.SnapshotId == row.Id)
            .ToListAsync(ct);
        foreach (var source in sources)
        {
            if (source.SourceId == WealthSourceIds.Banking)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE public.net_worth_snapshot_sources SET total = {source.Total + day.BankingDelta}, cash = {source.Cash + day.BankingDelta}
                    WHERE "Id" = {source.Id}
                    """, ct);
            }
            else if (source.SourceId == WealthSourceIds.Brokerage && day.BrokerageRederived)
            {
                // Positions x prior close is an estimate (ESTIMATED_VALUE, today's-rate FX) and was not measured that day (STALE_SOURCE).
                string[] reasons = [.. source.Reasons.Append(SourceReasons.EstimatedValue).Append(SourceReasons.StaleSource).Append(FxStatus.TodayRate.ToReason()!).Distinct()];
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE public.net_worth_snapshot_sources SET total = {repaired.Brokerage}, invested = {repaired.BrokerageInvested},
                        health = {SourceHealth.Estimated.ToString()}, reasons = {reasons}
                    WHERE "Id" = {source.Id}
                    """, ct);
            }
        }
    }
}
