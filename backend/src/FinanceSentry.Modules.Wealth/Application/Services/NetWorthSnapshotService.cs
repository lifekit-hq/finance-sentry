namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
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

        // A sleeve whose source failed to read has nothing to record and, with no earlier snapshot, nothing to
        // carry forward: fail the run (the job retries) instead of persisting it as $0 over a correct figure.
        if (previous is null
            && data.Sources?.FirstOrDefault(s => s.Reading is null && s.SourceId is "banking" or "brokerage" or "crypto") is { } failed)
            throw new InvalidOperationException(
                $"Wealth source '{failed.SourceId}' could not be read and there is no earlier snapshot to carry its sleeve forward from.");

        var banking = ResolveSleeve("banking", data.BankingTotal, data.BankingFresh, previous?.BankingTotal, stale);
        var brokerage = ResolveSleeve("brokerage", data.BrokerageTotal, data.BrokerageFresh, previous?.BrokerageTotal, stale);
        var crypto = ResolveSleeve("crypto", data.CryptoTotal, data.CryptoFresh, previous?.CryptoTotal, stale);

        // The total is every registered source's resolved figure: the three sleeves above, plus each other source
        // as it read this run - or, when its read failed, as its own previous per-source row counted it.
        var carriedSources = await CarryForwardUnreadSourcesAsync(userId, previous, data.Sources, ct);
        var otherSources = (data.Sources ?? []).Where(s => !SnapshotSources.SleeveIds.Contains(s.SourceId)).ToList();
        var otherTotal = otherSources.Sum(s => s.Reading?.TotalValueUsd ?? carriedSources.GetValueOrDefault(s.SourceId)?.Total ?? 0m);
        var otherCash = otherSources.Sum(s => s.Reading?.CashUsd ?? carriedSources.GetValueOrDefault(s.SourceId)?.Cash ?? 0m);
        var total = banking + brokerage + crypto + otherTotal;
        var brokerageInvested = ResolveInvested(data.BrokerageTotal, data.BrokerageInvested, brokerage, previous?.BrokerageInvested, stale.Contains("brokerage"));
        var cryptoInvested = ResolveInvested(data.CryptoTotal, data.CryptoInvested, crypto, previous?.CryptoInvested, stale.Contains("crypto"));
        var hasSplit = brokerageInvested is not null && cryptoInvested is not null;

        var fxStatus = data.FxStatus;
        if ((stale.Count > 0 || carriedSources.Count > 0) && previous is not null)
            fxStatus = fxStatus.Worst(previous.FxStatus ?? (previous.IsApproximate ? FxStatus.TodayRate : FxStatus.Exact));

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
            // Cash is every source's cash: the banking balance, each of the two sleeves without its invested part
            // (carried with the sleeve when it was carried forward), and the cash of each other source as it read
            // or was carried. Another source's invested value stays on its own per-source row. One unknown
            // invested part leaves the whole split null rather than half-filled.
            CashTotal = hasSplit ? banking + (brokerage - brokerageInvested!.Value) + (crypto - cryptoInvested!.Value) + otherCash : null,
            BrokerageInvested = hasSplit ? brokerageInvested : null,
            CryptoInvested = hasSplit ? cryptoInvested : null,
            FxStatus = fxStatus,
        };

        // Upsert: the day's row is refreshed on every successful sync rather than frozen
        // at first write, so the chart's newest point tracks the live position. The per-source rows go in the same
        // transaction, so a snapshot never exists with some of its sources.
        if (data.Sources is null)
            await _repository.UpsertAsync(snapshot, ct);
        else
            await _repository.UpsertWithSourcesAsync(snapshot, data.Sources.Select(f => carriedSources.GetValueOrDefault(f.SourceId) ?? ToRow(f, stale)).ToList(), ct);
    }

    /// <summary>
    /// The rows for the sources outside the three sleeve columns whose read failed this run and which have a
    /// figure on the previous snapshot: that figure is carried forward (DEGRADED + STALE_SOURCE), so the total
    /// does not drop by the source's whole value on a bad day. A sleeve is carried by its column instead; a source
    /// with no earlier figure contributes nothing and stays UNAVAILABLE.
    /// </summary>
    private async Task<Dictionary<string, NetWorthSnapshotSource>> CarryForwardUnreadSourcesAsync(
        Guid userId, NetWorthSnapshot? previous, IReadOnlyList<BookSourceReading>? figures, CancellationToken ct)
    {
        var unread = (figures ?? [])
            .Where(f => f.Reading is null && !SnapshotSources.SleeveIds.Contains(f.SourceId))
            .ToList();
        var carried = new Dictionary<string, NetWorthSnapshotSource>(StringComparer.Ordinal);
        if (unread.Count == 0 || previous is null)
            return carried;

        var earlier = (await _repository.GetSourcesUnscopedAsync(userId, [previous.Id], ct))
            .ToDictionary(r => r.SourceId, StringComparer.Ordinal);
        foreach (var figure in unread)
        {
            if (!earlier.TryGetValue(figure.SourceId, out var row) || row.Total is null)
                continue;

            carried[figure.SourceId] = new NetWorthSnapshotSource
            {
                SourceId = figure.SourceId,
                Kind = figure.Kind,
                Total = row.Total,
                Cash = row.Cash,
                Invested = row.Invested,
                Health = SourceHealth.Degraded > row.Health ? SourceHealth.Degraded : row.Health,
                Reasons = [.. row.Reasons.Append(SourceReasons.StaleSource).Distinct()],
            };
        }

        return carried;
    }

    /// <summary>
    /// A source's row, as the source read this run. A failed read has no figures (null, never zero) and is
    /// UNAVAILABLE + STALE_SOURCE, the way <see cref="BookFigures"/> reports it; a read that succeeded takes its FX
    /// reasons (ESTIMATED for today's/seed rate, DEGRADED for a 1:1 conversion), ESTIMATED + ESTIMATED_VALUE for an
    /// estimated (L3) value, and a sleeve this snapshot carried
    /// forward is at best DEGRADED + STALE_SOURCE. The snapshot's own column then holds the carried figure.
    /// </summary>
    private static NetWorthSnapshotSource ToRow(BookSourceReading figure, List<string> staleSleeves)
    {
        var reasons = new List<string>();
        var health = SourceHealth.Complete;
        if (figure.Reading is { } reading)
        {
            reasons.AddRange(reading.FxStatus.ToReasons());
            health = reading.FxStatus switch
            {
                FxStatus.Unknown1To1 => SourceHealth.Degraded,
                FxStatus.TodayRate or FxStatus.SeedTable => SourceHealth.Estimated,
                _ => SourceHealth.Complete,
            };
            if (reading.Reliability == Reliability.L3Estimated && reading.TotalValueUsd != 0m)
            {
                health = SourceHealth.Estimated > health ? SourceHealth.Estimated : health;
                reasons.Add(SourceReasons.EstimatedValue);
            }
            if (staleSleeves.Contains(figure.SourceId))
            {
                health = SourceHealth.Degraded > health ? SourceHealth.Degraded : health;
                reasons.Add(SourceReasons.StaleSource);
            }
        }
        else
        {
            health = SourceHealth.Unavailable;
            reasons.Add(SourceReasons.StaleSource);
        }

        return new NetWorthSnapshotSource
        {
            SourceId = figure.SourceId,
            Kind = figure.Kind,
            Total = figure.Reading?.TotalValueUsd,
            Cash = figure.Reading?.CashUsd,
            Invested = figure.Reading?.InvestedValueUsd,
            Health = health,
            Reasons = [.. reasons],
        };
    }

    /// <summary>
    /// The invested part of a sleeve: as measured this run, or - when the sleeve was carried forward - the
    /// previous snapshot's invested part. A carried sleeve that is empty (carried total 0, nothing measured
    /// this run) has nothing invested, so its split starts at 0 even when the previous row predates the split.
    /// </summary>
    private static decimal? ResolveInvested(
        decimal measuredTotal, decimal? measuredInvested, decimal recordedTotal, decimal? previousInvested, bool carriedForward)
    {
        if (!carriedForward)
            return measuredInvested;

        var emptySleeve = recordedTotal == 0m && measuredTotal == 0m && measuredInvested == 0m;
        return previousInvested ?? (emptySleeve ? 0m : null);
    }

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
