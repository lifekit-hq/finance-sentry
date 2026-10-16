using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.CryptoSync.Application.Services;

/// <summary>
/// <see cref="ICryptoFlowReader"/> over the movements the venues reported (<see cref="CryptoFlowEvent"/>), classified
/// for the crypto sleeve by <see cref="CryptoFlowClassifier"/>. A fiat leg is converted to USD at its own date's rate
/// (rule 5); a crypto leg at the venue price taken when it was read, which is the date's own only when it was read
/// that day. A venue's link day is the day its credential was first linked, which a reconnect does not move;
/// movements on or before it are history the linked value already holds. A venue's flows are known once its walk has
/// caught up (<see cref="CaughtUpLag"/>) - for Binance, its coin walk as well.
/// <para>
/// A coin one venue sent and another received (<see cref="CryptoTransferMatcher"/>) is a move inside the sleeve: no
/// flow, only the fee it cost, and the value is in transit from the send's day until the receipt's. A send no other
/// venue has received yet stays in transit while the other venue's walk has not read a day past it; after that it is
/// an external outflow. An unmatched transfer leg read as external while another venue is linked carries
/// <c>UNMATCHED_TRANSFER</c>: it may be a move inside the sleeve that the venues' records could not pair.
/// </para>
/// </summary>
public sealed class CryptoFlowReader(CryptoSyncDbContext db, IHistoricalExchangeRateService fx, TimeProvider timeProvider) : ICryptoFlowReader
{
    private const string UsdCode = "USD";

    /// <summary>How far behind now a venue's walk may be and still count as having read every movement.</summary>
    public static readonly TimeSpan CaughtUpLag = TimeSpan.FromDays(2);

    public async Task<SleeveFlowReading> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default)
    {
        var links = await db.ExchangeCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.UserId == userId && c.IsActive)
            .Select(c => new { c.Provider, c.FirstLinkedAt, c.FlowsWalkedTo, c.ChainFlowsWalkedTo })
            .ToListAsync(ct);
        if (links.Count == 0)
        {
            return SleeveFlowReading.Empty;
        }

        var linkedOn = links.ToDictionary(l => l.Provider, l => DateOnly.FromDateTime(l.FirstLinkedAt), StringComparer.Ordinal);
        // How far each venue's transfer legs have been read: a send not received by then never was.
        var transfersReadTo = links
            .GroupBy(l => l.Provider, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(l => l.Provider == CryptoExchangeProvider.Binance ? l.ChainFlowsWalkedTo : l.FlowsWalkedTo)
                    .Aggregate((DateTime?)DateTime.MaxValue, (min, at) => min is { } m && at is { } a ? (a < m ? a : m) : null),
                StringComparer.Ordinal);
        var rows = await db.CryptoFlowEvents.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .ToListAsync(ct);

        bool InSleeve(CryptoFlowEvent row) =>
            linkedOn.TryGetValue(row.Provider, out var link) && DateOnly.FromDateTime(row.OccurredAt) > link;

        // A pair is a move inside the sleeve only when both venues held their leg; otherwise each leg crossed its edge
        // (matched, so not flagged as unmatched).
        var matches = CryptoTransferMatcher.Match(rows);
        var matched = matches.SelectMany(p => new[] { p.Sent.Id, p.Received.Id }).ToHashSet();
        var pairs = matches.Where(p => InSleeve(p.Sent) && InSleeve(p.Received)).ToList();
        var pairOf = pairs
            .SelectMany(p => new[] { (p.Sent.Id, Pair: p), (p.Received.Id, Pair: p) })
            .ToDictionary(x => x.Id, x => x.Pair);

        var inScope = rows
            .Select(r => new { Row = r, Date = DateOnly.FromDateTime(r.OccurredAt) })
            .Where(r => InSleeve(r.Row) && r.Date > from)
            .OrderBy(r => r.Date)
            .ToList();

        var seriesByCurrency = new Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>>(StringComparer.Ordinal);
        foreach (var group in inScope.Where(r => r.Row.IsFiat && r.Row.Unit != UsdCode).GroupBy(r => r.Row.Unit))
        {
            seriesByCurrency[group.Key] = await fx.GetPublishedSeriesAsync(group.Key, group.Min(r => r.Date), group.Max(r => r.Date), ct);
        }

        var flows = new List<SourceFlow>();
        var inTransit = new List<InTransitValue>();
        var unpriced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in inScope)
        {
            var row = item.Row;
            var direction = CryptoFlowClassifier.ChainTransfer(row);
            var otherVenues = transfersReadTo.Keys.Where(p => p != row.Provider).ToList();
            IReadOnlyList<CryptoFlowLeg> legs;
            var unmatched = false;
            if (pairOf.TryGetValue(row.Id, out var pair))
            {
                // Matched: what the move cost, booked on the send; the receipt moves nothing across the edge.
                legs = row.Id == pair.Sent.Id ? MatchedSendLegs(pair) : [];
            }
            else if (direction == CryptoTransferDirection.Out
                && otherVenues.Any(p => transfersReadTo[p] is not { } readTo || readTo < row.OccurredAt + CryptoTransferMatcher.ArrivalWindow))
            {
                // Not received yet, and the other venue has not read far enough to say it never will be.
                legs = [.. CryptoFlowClassifier.AtSleeve(row).Where(l => l.Class == FlowClass.Fee)];
                if (CryptoToUsd(new CryptoFlowLeg(FlowClass.Internal, row.Amount, row.Unit), row, item.Date) is { } held)
                {
                    inTransit.Add(new InTransitValue(row.Provider, item.Date, null, held.Amount, held.Status));
                }
                else
                {
                    unpriced.Add(row.Provider);
                }
            }
            else
            {
                legs = CryptoFlowClassifier.AtSleeve(row);
                unmatched = direction is not null && otherVenues.Count > 0 && !matched.Contains(row.Id);
            }

            foreach (var leg in legs)
            {
                ConvertResult? usd = row.IsFiat
                    ? await FiatToUsdAsync(leg, item.Date, seriesByCurrency, ct)
                    : CryptoToUsd(leg, row, item.Date);
                if (usd is not { } converted)
                {
                    unpriced.Add(row.Provider);
                    continue;
                }

                flows.Add(new SourceFlow(
                    row.Provider,
                    new ClassifiedFlow(item.Date, leg.Class, converted.Amount),
                    converted.Status,
                    unmatched && leg.Class != FlowClass.Fee));
            }
        }

        foreach (var pair in pairs)
        {
            var sentOn = DateOnly.FromDateTime(pair.Sent.OccurredAt);
            var receivedOn = DateOnly.FromDateTime(pair.Received.OccurredAt);
            if (sentOn >= receivedOn || receivedOn <= from)
            {
                continue;
            }

            // In transit from the send's close to the receipt's day, at what arrived.
            var price = pair.Sent.UnitPriceUsd is not null ? pair.Sent : pair.Received;
            if (CryptoToUsd(new CryptoFlowLeg(FlowClass.Internal, pair.Received.Amount, price.Unit), price, sentOn) is { } held)
            {
                inTransit.Add(new InTransitValue(pair.Sent.Provider, sentOn, receivedOn, held.Amount, held.Status));
            }
            else
            {
                unpriced.Add(pair.Sent.Provider);
            }
        }

        // A venue's flows are known once its walks have caught up and every one could be valued.
        var caughtUpFrom = timeProvider.GetUtcNow().UtcDateTime - CaughtUpLag;
        var sources = links
            .GroupBy(l => l.Provider, StringComparer.Ordinal)
            .Select(g => new FlowSourceCoverage(
                g.Key,
                linkedOn[g.Key],
                g.All(l => l.FlowsWalkedTo >= caughtUpFrom
                        && (l.Provider != CryptoExchangeProvider.Binance || l.ChainFlowsWalkedTo >= caughtUpFrom))
                    && !unpriced.Contains(g.Key)))
            .OrderBy(s => s.Source, StringComparer.Ordinal)
            .ToList();

        return new SleeveFlowReading(sources, flows) { InTransit = inTransit };
    }

    /// <summary>
    /// A matched send's legs: the fee the move cost in the sent unit (sent less received) and any fee the venue kept
    /// in another unit.
    /// </summary>
    private static IReadOnlyList<CryptoFlowLeg> MatchedSendLegs(CryptoTransferPair pair)
    {
        var legs = new List<CryptoFlowLeg>(2);
        if (pair.Fee > 0m)
        {
            legs.Add(new CryptoFlowLeg(FlowClass.Fee, pair.Fee, pair.Sent.Unit));
        }

        legs.AddRange(CryptoFlowClassifier.AtSleeve(pair.Sent)
            .Where(l => l.Class == FlowClass.Fee && !string.Equals(l.Unit, pair.Sent.Unit, StringComparison.OrdinalIgnoreCase)));
        return legs;
    }

    private async Task<ConvertResult> FiatToUsdAsync(
        CryptoFlowLeg leg,
        DateOnly date,
        Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>> seriesByCurrency,
        CancellationToken ct)
    {
        if (leg.Unit == UsdCode)
        {
            return new ConvertResult(leg.Amount, FxStatus.Exact);
        }

        var published = seriesByCurrency.TryGetValue(leg.Unit, out var series)
            ? PublishedRateLookup.Resolve(series, date)
            : null;
        return published is { } rate
            ? new ConvertResult(leg.Amount * rate.Rate, rate.Status)
            : await fx.ConvertToUsdAsync(leg.Amount, leg.Unit, date, ct);
    }

    // shortcut: a crypto leg is priced at read time, so a walk that catches up on older transfers values them at
    // today's price (flagged TodayRate); dated candles would value them at their own day.
    private static ConvertResult? CryptoToUsd(CryptoFlowLeg leg, CryptoFlowEvent row, DateOnly date)
    {
        if (row.UnitPriceUsd is not { } price || leg.Unit != row.Unit)
        {
            return null;
        }

        var status = DateOnly.FromDateTime(row.RecordedAt) == date ? FxStatus.Exact : FxStatus.TodayRate;
        return new ConvertResult(leg.Amount * price, status);
    }
}
