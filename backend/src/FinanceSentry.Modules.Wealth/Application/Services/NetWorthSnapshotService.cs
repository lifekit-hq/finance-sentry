namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

public class NetWorthSnapshotService(INetWorthSnapshotRepository repository) : INetWorthSnapshotService
{
    private readonly INetWorthSnapshotRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task PersistSnapshotAsync(Guid userId, NetWorthSnapshotData data, CancellationToken ct = default)
    {
        // Never let a stale/missing/failed sync write a $0 or stale sleeve as if it were
        // real movement. When a sleeve isn't trustworthy this run, carry forward its last
        // known-good value and record which sleeves were estimated so trend analysis can
        // tell a measured net worth from a partially carried-forward one. The baseline is
        // the latest snapshot BEFORE the target date — same-day refreshes must not carry
        // forward from themselves.
        var previous = await _repository.GetLatestBeforeUnscopedAsync(userId, data.SnapshotDate, ct);
        var stale = new List<string>();

        var banking = ResolveSleeve("banking", data.BankingTotal, data.BankingFresh, previous?.BankingTotal, stale);
        var brokerage = ResolveSleeve("brokerage", data.BrokerageTotal, data.BrokerageFresh, previous?.BrokerageTotal, stale);
        var crypto = ResolveSleeve("crypto", data.CryptoTotal, data.CryptoFresh, previous?.CryptoTotal, stale);

        var total = banking + brokerage + crypto;
        var brokerageInvested = ResolveInvested(data.BrokerageInvested, previous?.BrokerageInvested, stale.Contains("brokerage"));
        var cryptoInvested = ResolveInvested(data.CryptoInvested, previous?.CryptoInvested, stale.Contains("crypto"));
        var hasSplit = brokerageInvested is not null && cryptoInvested is not null;

        var snapshot = new NetWorthSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SnapshotDate = data.SnapshotDate,
            BankingTotal = banking,
            BrokerageTotal = brokerage,
            CryptoTotal = crypto,
            TotalNetWorth = total,
            Currency = data.Currency,
            TakenAt = DateTimeOffset.UtcNow,
            StaleSleeves = stale.Count > 0 ? string.Join(',', stale) : null,
            // Cash is the remainder, so cash + invested always equals the stored total - including when a
            // sleeve was carried forward (its invested part is carried with it; a carried banking balance
            // stays in cash). One unknown invested part leaves the whole split null rather than half-filled.
            CashTotal = hasSplit ? total - brokerageInvested!.Value - cryptoInvested!.Value : null,
            BrokerageInvested = hasSplit ? brokerageInvested : null,
            CryptoInvested = hasSplit ? cryptoInvested : null,
        };

        // Upsert: the day's row is refreshed on every successful sync rather than frozen
        // at first write, so the chart's newest point tracks the live position.
        await _repository.UpsertAsync(snapshot, ct);
    }

    /// <summary>
    /// The invested part of a sleeve: as measured this run, or - when the sleeve was carried forward - the
    /// previous snapshot's invested part (null if that had no split).
    /// </summary>
    private static decimal? ResolveInvested(decimal? measured, decimal? previous, bool carriedForward)
        => carriedForward ? previous : measured;

    /// <summary>
    /// Returns the value to record for a sleeve. Uses the fresh value when the feed is
    /// trustworthy; otherwise carries forward the previous snapshot's value (and flags the
    /// sleeve as stale). Treats a drop to exactly $0 when we previously held value as a
    /// failed sync rather than a genuine liquidation.
    /// </summary>
    private static decimal ResolveSleeve(string name, decimal freshValue, bool isFresh, decimal? previousValue, List<string> stale)
    {
        var looksLikeFailedSync = freshValue == 0m && previousValue is > 0m;

        if (isFresh && !looksLikeFailedSync)
            return freshValue;

        if (previousValue is null)
            return freshValue; // no history to fall back on — best effort with what we have

        stale.Add(name);
        return previousValue.Value;
    }

}
