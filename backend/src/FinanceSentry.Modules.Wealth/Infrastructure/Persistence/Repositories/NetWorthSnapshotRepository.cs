namespace FinanceSentry.Modules.Wealth.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

// Reads run under the Owner filter. The snapshot job, the startup catch-up and the cross-module brokerage history
// reader run with no person in scope, so they call the <c>…Unscoped…</c> methods, which opt out explicitly and keep
// their own UserId predicate.
public class NetWorthSnapshotRepository(WealthDbContext db) : INetWorthSnapshotRepository
{
    private readonly WealthDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task PersistAsync(NetWorthSnapshot snapshot, CancellationToken ct = default)
    {
        _db.NetWorthSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);
    }

    // One INSERT ... ON CONFLICT on the unique (UserId, SnapshotDate) index: the day's row is refreshed in place, so
    // two snapshots of one day leave one row instead of the loser failing on the index, and no read precedes it. The
    // row keeps its Id. It runs at once and the snapshot is not tracked by this context.
    public async Task UpsertAsync(NetWorthSnapshot snapshot, CancellationToken ct = default)
        => await UpsertSnapshotAsync(snapshot, ct);

    // The upsert, the delete of the day's earlier source rows and the inserts share one transaction: the upsert
    // holds the snapshot row's lock until commit, so two writers of one day are serialized and a reader never sees a
    // snapshot with half its sources.
    public async Task UpsertWithSourcesAsync(
        NetWorthSnapshot snapshot, IReadOnlyList<NetWorthSnapshotSource> sources, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await UpsertSnapshotAsync(snapshot, ct);

        // The row keeps the Id of the day's first write, so the children hang off whichever Id the table holds now.
        var snapshotId = (await _db.Database.SqlQuery<Guid>(
            $"""
            SELECT "Id" AS "Value" FROM public.net_worth_snapshots
            WHERE "UserId" = {snapshot.UserId} AND "SnapshotDate" = {snapshot.SnapshotDate}
            """).ToListAsync(ct)).Single();

        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM public.net_worth_snapshot_sources WHERE snapshot_id = {snapshotId}", ct);

        foreach (var source in sources)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public.net_worth_snapshot_sources
                    ("Id", snapshot_id, source_id, kind, total, cash, invested, health, reasons)
                VALUES
                    ({Guid.NewGuid()}, {snapshotId}, {source.SourceId}, {source.Kind.ToString()}, {source.Total},
                     {source.Cash}, {source.Invested}, {source.Health.ToString()}, {source.Reasons})
                """,
                ct);
        }

        await transaction.CommitAsync(ct);
    }

    private async Task UpsertSnapshotAsync(NetWorthSnapshot snapshot, CancellationToken ct)
        => await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.net_worth_snapshots
                ("Id", "UserId", "SnapshotDate", "BankingTotal", "BrokerageTotal", "CryptoTotal", "TotalNetWorth",
                 "Currency", "TakenAt", stale_sleeves, is_approximate, cash_total, brokerage_invested, crypto_invested,
                 fx_status)
            VALUES
                ({snapshot.Id}, {snapshot.UserId}, {snapshot.SnapshotDate}, {snapshot.BankingTotal},
                 {snapshot.BrokerageTotal}, {snapshot.CryptoTotal}, {snapshot.TotalNetWorth}, {snapshot.Currency},
                 {snapshot.TakenAt.ToUniversalTime()}, {snapshot.StaleSleeves}, {snapshot.IsApproximate},
                 {snapshot.CashTotal}, {snapshot.BrokerageInvested}, {snapshot.CryptoInvested}, {(int?)snapshot.FxStatus})
            ON CONFLICT ("UserId", "SnapshotDate") DO UPDATE SET
                "BankingTotal" = EXCLUDED."BankingTotal",
                "BrokerageTotal" = EXCLUDED."BrokerageTotal",
                "CryptoTotal" = EXCLUDED."CryptoTotal",
                "TotalNetWorth" = EXCLUDED."TotalNetWorth",
                "Currency" = EXCLUDED."Currency",
                "TakenAt" = EXCLUDED."TakenAt",
                stale_sleeves = EXCLUDED.stale_sleeves,
                is_approximate = EXCLUDED.is_approximate,
                cash_total = EXCLUDED.cash_total,
                brokerage_invested = EXCLUDED.brokerage_invested,
                crypto_invested = EXCLUDED.crypto_invested,
                fx_status = EXCLUDED.fx_status
            """,
            ct);

    public Task<bool> ExistsAsync(Guid userId, DateOnly snapshotDate, CancellationToken ct = default)
        => _db.NetWorthSnapshots.AnyAsync(s => s.UserId == userId && s.SnapshotDate == snapshotDate, ct);

    public Task<NetWorthSnapshot?> GetLatestBeforeUnscopedAsync(Guid userId, DateOnly date, CancellationToken ct = default)
        => _db.NetWorthSnapshots.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(s => s.UserId == userId && s.SnapshotDate < date)
            .OrderByDescending(s => s.SnapshotDate)
            .ThenByDescending(s => s.TakenAt)
            .FirstOrDefaultAsync(ct);

    public Task<NetWorthSnapshot?> GetLatestByUserIdAsync(Guid userId, CancellationToken ct = default)
        => LatestByUserId(_db.NetWorthSnapshots, userId, ct);

    public Task<NetWorthSnapshot?> GetLatestByUserIdUnscopedAsync(Guid userId, CancellationToken ct = default)
        => LatestByUserId(_db.NetWorthSnapshots.IgnoreQueryFilters([OwnerQueryFilter.Name]), userId, ct);

    public Task<IReadOnlyList<NetWorthSnapshot>> GetByUserIdAsync(Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        => ByUserId(_db.NetWorthSnapshots, userId, from, to, ct);

    public Task<IReadOnlyList<NetWorthSnapshot>> GetByUserIdUnscopedAsync(Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        => ByUserId(_db.NetWorthSnapshots.IgnoreQueryFilters([OwnerQueryFilter.Name]), userId, from, to, ct);

    public Task<IReadOnlyList<NetWorthSnapshotSource>> GetSourcesAsync(
        Guid userId, IReadOnlyCollection<Guid> snapshotIds, CancellationToken ct = default)
        => SourcesOf(_db.NetWorthSnapshotSources, _db.NetWorthSnapshots, userId, snapshotIds, ct);

    public Task<IReadOnlyList<NetWorthSnapshotSource>> GetSourcesUnscopedAsync(
        Guid userId, IReadOnlyCollection<Guid> snapshotIds, CancellationToken ct = default)
        => SourcesOf(
            _db.NetWorthSnapshotSources.IgnoreQueryFilters([OwnerQueryFilter.Name]),
            _db.NetWorthSnapshots.IgnoreQueryFilters([OwnerQueryFilter.Name]),
            userId, snapshotIds, ct);

    public Task<NetWorthSnapshot?> GetEarliestByUserIdAsync(Guid userId, CancellationToken ct = default)
        => _db.NetWorthSnapshots
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.SnapshotDate)
            .ThenBy(s => s.TakenAt)
            .FirstOrDefaultAsync(ct);

    // One INSERT ... ON CONFLICT DO NOTHING per row on the unique (UserId, SnapshotDate) index, in one transaction: a
    // day that already has a row is never overwritten, a re-run inserts nothing, and a concurrent writer of the same
    // day makes the loser skip it instead of failing on the index.
    public async Task<int> InsertMissingAsync(IReadOnlyCollection<NetWorthSnapshot> snapshots, CancellationToken ct = default)
    {
        if (snapshots.Count == 0)
            return 0;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var inserted = 0;
        foreach (var snapshot in snapshots)
        {
            inserted += await _db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public.net_worth_snapshots
                    ("Id", "UserId", "SnapshotDate", "BankingTotal", "BrokerageTotal", "CryptoTotal", "TotalNetWorth",
                     "Currency", "TakenAt", stale_sleeves, is_approximate, cash_total, brokerage_invested,
                     crypto_invested, fx_status)
                VALUES
                    ({snapshot.Id}, {snapshot.UserId}, {snapshot.SnapshotDate}, {snapshot.BankingTotal},
                     {snapshot.BrokerageTotal}, {snapshot.CryptoTotal}, {snapshot.TotalNetWorth}, {snapshot.Currency},
                     {snapshot.TakenAt.ToUniversalTime()}, {snapshot.StaleSleeves}, {snapshot.IsApproximate},
                     {snapshot.CashTotal}, {snapshot.BrokerageInvested}, {snapshot.CryptoInvested},
                     {(int?)snapshot.FxStatus})
                ON CONFLICT ("UserId", "SnapshotDate") DO NOTHING
                """,
                ct);
        }

        await transaction.CommitAsync(ct);
        return inserted;
    }

    public async Task<int> FillMissingSplitAsync(Guid userId, IReadOnlyCollection<NetWorthSplit> splits, CancellationToken ct = default)
    {
        if (splits.Count == 0)
            return 0;

        var byDate = splits.ToDictionary(s => s.SnapshotDate);
        var from = byDate.Keys.Min();
        var to = byDate.Keys.Max();
        var rows = await _db.NetWorthSnapshots.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(s => s.UserId == userId && s.SnapshotDate >= from && s.SnapshotDate <= to)
            .ToListAsync(ct);

        var changed = 0;
        foreach (var row in rows)
        {
            if (!byDate.TryGetValue(row.SnapshotDate, out var split))
                continue;

            var entry = _db.Entry(row);
            var touched = FillIfNull(entry.Property(s => s.CashTotal), split.CashTotal)
                | FillIfNull(entry.Property(s => s.BrokerageInvested), split.BrokerageInvested)
                | FillIfNull(entry.Property(s => s.CryptoInvested), split.CryptoInvested);
            if (touched)
                changed++;
        }

        if (changed > 0)
            await _db.SaveChangesAsync(ct);
        return changed;
    }

    private static bool FillIfNull(PropertyEntry<NetWorthSnapshot, decimal?> property, decimal value)
    {
        if (property.CurrentValue is not null)
            return false;
        property.CurrentValue = value;
        return true;
    }

    private static Task<NetWorthSnapshot?> LatestByUserId(
        IQueryable<NetWorthSnapshot> snapshots, Guid userId, CancellationToken ct)
        => snapshots
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.SnapshotDate)
            .ThenByDescending(s => s.TakenAt)
            .FirstOrDefaultAsync(ct);

    // The rows belong to whoever owns their snapshot, so the user predicate goes through the parent.
    private static async Task<IReadOnlyList<NetWorthSnapshotSource>> SourcesOf(
        IQueryable<NetWorthSnapshotSource> sources, IQueryable<NetWorthSnapshot> snapshots,
        Guid userId, IReadOnlyCollection<Guid> snapshotIds, CancellationToken ct)
    {
        if (snapshotIds.Count == 0)
            return [];

        var ids = snapshotIds.ToList();
        return await sources
            .Where(x => ids.Contains(x.SnapshotId) && snapshots.Any(s => s.Id == x.SnapshotId && s.UserId == userId))
            .ToListAsync(ct);
    }

    private static async Task<IReadOnlyList<NetWorthSnapshot>> ByUserId(
        IQueryable<NetWorthSnapshot> snapshots, Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var query = snapshots.Where(s => s.UserId == userId);

        if (from.HasValue)
            query = query.Where(s => s.SnapshotDate >= from.Value);

        if (to.HasValue)
            query = query.Where(s => s.SnapshotDate <= to.Value);

        return await query.OrderBy(s => s.SnapshotDate).ToListAsync(ct);
    }
}
