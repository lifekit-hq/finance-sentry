namespace FinanceSentry.Modules.Wealth.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

public sealed class NetWorthSnapshotBackfillService(
    IBankingTotalsReader bankingTotalsReader,
    INetWorthSnapshotRepository snapshotRepository,
    NetWorthSnapshotJob snapshotJob)
{
    private static readonly TimeSpan StaleSnapshotThreshold = TimeSpan.FromHours(20);

    private readonly IBankingTotalsReader _bankingTotalsReader = bankingTotalsReader ?? throw new ArgumentNullException(nameof(bankingTotalsReader));
    private readonly INetWorthSnapshotRepository _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
    private readonly NetWorthSnapshotJob _snapshotJob = snapshotJob ?? throw new ArgumentNullException(nameof(snapshotJob));

    public async Task BackfillAsync(CancellationToken ct = default)
    {
        var activeUserIds = await _bankingTotalsReader.GetActiveUserIdsAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;

        foreach (var userId in activeUserIds)
            await BackfillUserAsync(userId, today, now, ct);
    }

    private async Task BackfillUserAsync(Guid userId, DateOnly today, DateTimeOffset now, CancellationToken ct)
    {
        var latestSnapshot = await _snapshotRepository.GetLatestByUserIdUnscopedAsync(userId, ct);

        if (latestSnapshot is null)
        {
            await _snapshotJob.CaptureForUserAsync(userId, today, ct);
            return;
        }

        var backfillStart = latestSnapshot.SnapshotDate.AddDays(1);
        var backfillEnd = today.AddDays(-1);

        for (var date = backfillStart; date <= backfillEnd; date = date.AddDays(1))
            await _snapshotJob.CaptureForUserAsync(userId, date, ct);

        if (latestSnapshot.SnapshotDate < today && now - latestSnapshot.TakenAt >= StaleSnapshotThreshold)
            await _snapshotJob.CaptureForUserAsync(userId, today, ct);
    }
}
