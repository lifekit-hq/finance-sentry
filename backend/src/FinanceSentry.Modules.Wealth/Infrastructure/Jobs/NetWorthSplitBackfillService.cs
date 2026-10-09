namespace FinanceSentry.Modules.Wealth.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// Fills the cash/invested split of net-worth snapshots taken before the nightly capture wrote it, from the
/// invested positions <c>holding_snapshots</c> recorded: cash = the row's own total - that day's invested value.
/// The two are captured at different moments, so intraday moves can shift a little between cash and invested. It
/// only fills null columns (never overwrites), so it is idempotent. Days the history does not cover - and days
/// where a sleeve's stored total has no invested positions behind it (a source that was missing when Risk ran) -
/// keep a null split: "no split", not zero.
/// </summary>
/// <remarks>
/// Bounded: one history read and one snapshot read per user, over at most <see cref="HistoryWindowDays"/> days
/// (the table's retention); run from the worker's background catch-up, so it never delays startup.
/// </remarks>
public sealed class NetWorthSplitBackfillService(
    IBankingTotalsReader bankingTotalsReader,
    IInvestedHistoryReader investedHistory,
    INetWorthSnapshotRepository snapshotRepository)
{
    /// <summary>Retention of <c>holding_snapshots</c> (<c>RetentionPolicyRegistry</c>): nothing older exists to read.</summary>
    public const int HistoryWindowDays = 180;

    public async Task<int> BackfillAsync(CancellationToken ct = default)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-HistoryWindowDays);
        var filled = 0;
        foreach (var userId in await bankingTotalsReader.GetActiveUserIdsAsync(ct))
            filled += await BackfillUserAsync(userId, from, ct);
        return filled;
    }

    public async Task<int> BackfillUserAsync(Guid userId, DateOnly from, CancellationToken ct = default)
    {
        var days = await investedHistory.GetDailyAsync(userId, from, ct);
        if (days.Count == 0)
            return 0;

        var byDate = days.ToDictionary(d => d.Date);
        var snapshots = await snapshotRepository.GetByUserIdUnscopedAsync(userId, days.Min(d => d.Date), days.Max(d => d.Date), ct);

        var splits = snapshots
            .Where(s => !s.IsApproximate
                && !CarriesInvestedSleeve(s)
                && (s.CashTotal is null || s.BrokerageInvested is null || s.CryptoInvested is null)
                && byDate.TryGetValue(s.SnapshotDate, out var day)
                && !MissingSource(s, day))
            .Select(s =>
            {
                var invested = byDate[s.SnapshotDate];
                return new NetWorthSplit(
                    s.SnapshotDate,
                    s.TotalNetWorth - invested.BrokerageUsd - invested.CryptoUsd,
                    invested.BrokerageUsd,
                    invested.CryptoUsd);
            })
            .ToList();

        return await snapshotRepository.FillMissingSplitAsync(userId, splits, ct);
    }

    // A brokerage/crypto sleeve carried forward from an earlier day holds a value the day's captured positions
    // do not describe, so its remainder would not be cash. A carried banking balance is cash and is fine, and so
    // is an empty carried sleeve (nothing there to mis-describe).
    private static bool CarriesInvestedSleeve(NetWorthSnapshot snapshot)
    {
        var stale = snapshot.StaleSleeves?.Split(',') ?? [];
        return (stale.Contains("brokerage") && snapshot.BrokerageTotal != 0m)
            || (stale.Contains("crypto") && snapshot.CryptoTotal != 0m);
    }

    // Risk stores holding_snapshots even when a book source failed, leaving that sleeve with no positions and no
    // marker. A sleeve with value on the row but nothing invested behind it would land whole in cash, so the day
    // keeps its null split instead - the same rule as the nightly capture's stale book.
    private static bool MissingSource(NetWorthSnapshot snapshot, DailyInvestedBySleeve day)
        => (day.BrokerageUsd == 0m && snapshot.BrokerageTotal != 0m)
            || (day.CryptoUsd == 0m && snapshot.CryptoTotal != 0m);
}
