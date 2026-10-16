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
/// that day. A venue's link day is its active connection's creation day; movements on or before it are history the
/// linked value already holds.
/// </summary>
public sealed class CryptoFlowReader(CryptoSyncDbContext db, IHistoricalExchangeRateService fx) : ICryptoFlowReader
{
    private const string UsdCode = "USD";

    public async Task<SleeveFlowReading> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default)
    {
        var links = await db.ExchangeCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.UserId == userId && c.IsActive)
            .Select(c => new { c.Provider, c.CreatedAt, c.FlowsWalkedTo })
            .ToListAsync(ct);
        if (links.Count == 0)
        {
            return SleeveFlowReading.Empty;
        }

        var linkedOn = links.ToDictionary(l => l.Provider, l => DateOnly.FromDateTime(l.CreatedAt), StringComparer.Ordinal);
        var rows = await db.CryptoFlowEvents.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .ToListAsync(ct);
        var inScope = rows
            .Select(r => new { Row = r, Date = DateOnly.FromDateTime(r.OccurredAt) })
            .Where(r => linkedOn.TryGetValue(r.Row.Provider, out var link) && r.Date > link && r.Date > from)
            .OrderBy(r => r.Date)
            .ToList();

        var seriesByCurrency = new Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>>(StringComparer.Ordinal);
        foreach (var group in inScope.Where(r => r.Row.IsFiat && r.Row.Unit != UsdCode).GroupBy(r => r.Row.Unit))
        {
            seriesByCurrency[group.Key] = await fx.GetPublishedSeriesAsync(group.Key, group.Min(r => r.Date), group.Max(r => r.Date), ct);
        }

        var flows = new List<SourceFlow>();
        var unpriced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in inScope)
        {
            foreach (var leg in CryptoFlowClassifier.AtSleeve(item.Row))
            {
                ConvertResult? usd = item.Row.IsFiat
                    ? await FiatToUsdAsync(leg, item.Date, seriesByCurrency, ct)
                    : CryptoToUsd(leg, item.Row, item.Date);
                if (usd is not { } converted)
                {
                    unpriced.Add(item.Row.Provider);
                    continue;
                }

                flows.Add(new SourceFlow(item.Row.Provider, new ClassifiedFlow(item.Date, leg.Class, converted.Amount), converted.Status));
            }
        }

        // A venue's flows are known once a walk of them has succeeded and every one could be valued.
        // shortcut: Binance stays unknown until its on-chain, P2P and Pay legs are read (S2b): its fiat legs alone
        // would read a crypto deposit as growth.
        var sources = links
            .GroupBy(l => l.Provider, StringComparer.Ordinal)
            .Select(g => new FlowSourceCoverage(
                g.Key,
                linkedOn[g.Key],
                g.Key != CryptoExchangeProvider.Binance
                    && g.All(l => l.FlowsWalkedTo is not null)
                    && !unpriced.Contains(g.Key)))
            .OrderBy(s => s.Source, StringComparer.Ordinal)
            .ToList();

        return new SleeveFlowReading(sources, flows);
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
