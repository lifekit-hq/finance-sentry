namespace FinanceSentry.Modules.Wealth.Domain.Repositories;

public interface INetWorthSnapshotRepository
{
    Task PersistAsync(NetWorthSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// Inserts the snapshot, replacing any existing row for the same (user, date) — a
    /// day's snapshot is refreshed throughout the day rather than frozen at first write.
    /// Its existence check opts out of the Owner query filter: the snapshot job writes with no person in scope.
    /// </summary>
    Task UpsertAsync(NetWorthSnapshot snapshot, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid userId, DateOnly snapshotDate, CancellationToken ct = default);
    Task<NetWorthSnapshot?> GetLatestByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's latest snapshot, for the startup catch-up. Opts out of the Owner query filter.</summary>
    Task<NetWorthSnapshot?> GetLatestByUserIdUnscopedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Latest snapshot strictly before <paramref name="date"/> — the carry-forward baseline.
    /// Opts out of the Owner query filter.</summary>
    Task<NetWorthSnapshot?> GetLatestBeforeUnscopedAsync(Guid userId, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<NetWorthSnapshot>> GetByUserIdAsync(Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    /// <summary>The user's snapshots in the range, for the cross-module brokerage history reader.
    /// Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<NetWorthSnapshot>> GetByUserIdUnscopedAsync(Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    /// <summary>Earliest snapshot for the user — the boundary a history backfill reconstructs up to.</summary>
    Task<NetWorthSnapshot?> GetEarliestByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Inserts every given snapshot whose (UserId, SnapshotDate) is not already occupied,
    /// skipping the rest. Never replaces an existing row — real or previously backfilled —
    /// which is what makes a backfill run idempotent and safe to repeat.
    /// </summary>
    Task<int> InsertMissingAsync(IReadOnlyCollection<NetWorthSnapshot> snapshots, CancellationToken ct = default);

    /// <summary>
    /// Writes the cash/invested split onto existing rows, one entry per (user, date), setting only the columns that
    /// are still null - a value already on a row is never replaced, so re-running changes nothing. Rows with no entry
    /// are untouched. Opts out of the Owner query filter (a startup job runs with no person in scope). Returns the
    /// number of rows changed.
    /// </summary>
    Task<int> FillMissingSplitAsync(Guid userId, IReadOnlyCollection<NetWorthSplit> splits, CancellationToken ct = default);
}

/// <summary>The cash/invested split for one snapshot date. Cash + both invested parts equal that day's total.</summary>
public sealed record NetWorthSplit(DateOnly SnapshotDate, decimal CashTotal, decimal BrokerageInvested, decimal CryptoInvested);
