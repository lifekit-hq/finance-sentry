namespace FinanceSentry.Modules.Wealth.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class NetWorthSnapshotRepository(WealthDbContext db) : INetWorthSnapshotRepository
{
    private readonly WealthDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task PersistAsync(NetWorthSnapshot snapshot, CancellationToken ct = default)
    {
        _db.NetWorthSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpsertAsync(NetWorthSnapshot snapshot, CancellationToken ct = default)
    {
        var existing = await _db.NetWorthSnapshots
            .FirstOrDefaultAsync(s => s.UserId == snapshot.UserId && s.SnapshotDate == snapshot.SnapshotDate, ct);
        if (existing is not null)
            _db.NetWorthSnapshots.Remove(existing);

        _db.NetWorthSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> ExistsAsync(Guid userId, DateOnly snapshotDate, CancellationToken ct = default)
        => _db.NetWorthSnapshots.AnyAsync(s => s.UserId == userId && s.SnapshotDate == snapshotDate, ct);

    public Task<NetWorthSnapshot?> GetLatestBeforeAsync(Guid userId, DateOnly date, CancellationToken ct = default)
        => _db.NetWorthSnapshots
            .Where(s => s.UserId == userId && s.SnapshotDate < date)
            .OrderByDescending(s => s.SnapshotDate)
            .ThenByDescending(s => s.TakenAt)
            .FirstOrDefaultAsync(ct);

    public Task<NetWorthSnapshot?> GetLatestByUserIdAsync(Guid userId, CancellationToken ct = default)
        => _db.NetWorthSnapshots
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.SnapshotDate)
            .ThenByDescending(s => s.TakenAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<NetWorthSnapshot>> GetByUserIdAsync(Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var query = _db.NetWorthSnapshots.Where(s => s.UserId == userId);

        if (from.HasValue)
            query = query.Where(s => s.SnapshotDate >= from.Value);

        if (to.HasValue)
            query = query.Where(s => s.SnapshotDate <= to.Value);

        return await query.OrderBy(s => s.SnapshotDate).ToListAsync(ct);
    }

    public Task<NetWorthSnapshot?> GetEarliestByUserIdAsync(Guid userId, CancellationToken ct = default)
        => _db.NetWorthSnapshots
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.SnapshotDate)
            .ThenBy(s => s.TakenAt)
            .FirstOrDefaultAsync(ct);

    public async Task<int> InsertMissingAsync(IReadOnlyCollection<NetWorthSnapshot> snapshots, CancellationToken ct = default)
    {
        if (snapshots.Count == 0)
            return 0;

        var userId = snapshots.First().UserId;
        var existingDates = await _db.NetWorthSnapshots
            .Where(s => s.UserId == userId)
            .Select(s => s.SnapshotDate)
            .ToHashSetAsync(ct);

        var toInsert = snapshots.Where(s => !existingDates.Contains(s.SnapshotDate)).ToList();
        if (toInsert.Count == 0)
            return 0;

        _db.NetWorthSnapshots.AddRange(toInsert);
        await _db.SaveChangesAsync(ct);
        return toInsert.Count;
    }
}
