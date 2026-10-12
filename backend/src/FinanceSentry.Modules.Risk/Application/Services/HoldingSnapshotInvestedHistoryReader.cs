namespace FinanceSentry.Modules.Risk.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Repositories;

/// <summary>
/// Serves <see cref="IInvestedHistoryReader"/> from <c>holding_snapshots</c>. Captures are written once per
/// position per risk-check run, so a run is the set of rows sharing a <c>CapturedAt</c>; a day's figure is the
/// sum of its latest run, per sleeve (the end-of-day state, as the net-worth snapshot's own upserted row is).
/// </summary>
public sealed class HoldingSnapshotInvestedHistoryReader(IHoldingSnapshotRepository snapshots) : IInvestedHistoryReader
{
    public async Task<IReadOnlyList<DailyInvestedBySleeve>> GetDailyAsync(
        Guid userId, DateOnly from, CancellationToken ct = default)
    {
        var since = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rows = await snapshots.ListSinceUnscopedAsync(userId, since, ct);

        return rows
            .GroupBy(r => DateOnly.FromDateTime(r.CapturedAt.UtcDateTime))
            .OrderBy(day => day.Key)
            .Select(day =>
            {
                var latest = day.Max(r => r.CapturedAt);
                var run = day.Where(r => r.CapturedAt == latest).ToList();
                return new DailyInvestedBySleeve(
                    day.Key,
                    run.Where(r => r.Sleeve == RiskSleeve.Brokerage).Sum(r => r.UsdValue),
                    run.Where(r => r.Sleeve == RiskSleeve.Crypto).Sum(r => r.UsdValue));
            })
            .ToList();
    }
}
