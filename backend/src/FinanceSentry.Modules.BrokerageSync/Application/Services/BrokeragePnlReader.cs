namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// <see cref="IBrokeragePnlReader"/> over the persisted fills and the holdings' verified cost basis (issue 982).
/// Unrealized covers the open positions whose basis <see cref="BrokerageCostBasisReconciler"/> verified against the
/// fills (<see cref="IBrokerageHoldingsReader"/> already withholds the basis of every other position); the broker's
/// own average cost is never used. Realized is the <see cref="RealizedPnlWalk"/> sum over the symbols whose fills
/// explain their whole history. Cash rows and currency-conversion trades are not positions and are left out.
/// </summary>
public sealed class BrokeragePnlReader(BrokerageSyncDbContext db, IBrokerageHoldingsReader holdings) : IBrokeragePnlReader
{
    private const string VerifiedBasis = "Verified";
    private const int MoneyDecimals = 2;

    public async Task<BrokeragePnlReading> ReadAsync(Guid userId, DateOnly? realizedFrom, CancellationToken ct = default)
    {
        var positions = (await holdings.GetHoldingsAsync(userId, ct))
            .Where(h => h.Quantity > 0m && AssetClassNormalizer.Normalize(h.InstrumentType) != AssetClassNormalizer.Cash)
            .ToList();

        var realized = await ReadRealizedAsync(userId, positions, realizedFrom, ct);
        return new BrokeragePnlReading(realized, ReadUnrealized(positions));
    }

    private static UnrealizedPnlReading ReadUnrealized(IReadOnlyList<BrokerageHoldingSummary> positions)
    {
        var measured = positions
            .Where(p => p.BasisState == VerifiedBasis && p.CostBasisUsd is > 0m)
            .ToList();
        var costBasis = measured.Sum(p => p.CostBasisUsd!.Value);
        var measuredValue = measured.Sum(p => p.UsdValue);

        return new UnrealizedPnlReading(
            Usd: measuredValue - costBasis,
            CostBasisUsd: costBasis,
            MeasuredValueUsd: measuredValue,
            TotalValueUsd: positions.Sum(p => p.UsdValue),
            PositionsMeasured: measured.Count,
            PositionsTotal: positions.Count,
            UnverifiedSymbols: [.. positions.Except(measured)
                .Select(p => p.Symbol)
                .Order(StringComparer.OrdinalIgnoreCase)]);
    }

    private async Task<RealizedPnlReading> ReadRealizedAsync(
        Guid userId, IReadOnlyList<BrokerageHoldingSummary> positions, DateOnly? from, CancellationToken ct)
    {
        var trades = await db.BrokerageTrades.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(t => t.UserId == userId)
            .ToListAsync(ct);
        if (trades.Count == 0)
        {
            return new RealizedPnlReading(0m, 0, [], HasFills: false, FxStatus.Exact);
        }

        var cashInstruments = (await db.BrokerageInstruments.IgnoreQueryFilters([OwnerQueryFilter.Name])
                .Where(i => i.UserId == userId)
                .Select(i => new { i.Id, i.InstrumentType })
                .ToListAsync(ct))
            .Where(i => AssetClassNormalizer.Normalize(i.InstrumentType) == AssetClassNormalizer.Cash)
            .Select(i => i.Id)
            .ToHashSet();

        var held = positions
            .GroupBy(p => Key(p.Provider, p.Symbol))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Quantity));

        var total = 0m;
        var computed = 0;
        var fx = FxStatus.Exact;
        var unavailable = new List<UnavailableRealizedSymbol>();

        foreach (var group in trades
                     .Where(t => t.InstrumentId is not Guid id || !cashInstruments.Contains(id))
                     .GroupBy(t => Key(t.Provider, t.Symbol))
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var sample = group.First();
            var walk = RealizedPnlWalk.Walk(group, held.GetValueOrDefault(group.Key), from);
            if (!walk.SoldInWindow && walk.Explained)
            {
                continue;
            }

            // A symbol with an unexplained history only matters when it sold in the window; one whose history is
            // unexplained and whose sells are all before the window cannot change the figure either way.
            if (!walk.Explained)
            {
                if (HasSellInWindow(group, from))
                {
                    unavailable.Add(new UnavailableRealizedSymbol(sample.Provider, sample.Symbol));
                }

                continue;
            }

            total += walk.RealizedUsd;
            computed++;
            fx = fx.Worst(walk.Fx);
        }

        // The average-cost division leaves decimal dust past the cent; the figure is money, so it is stated to the cent.
        return new RealizedPnlReading(Math.Round(total, MoneyDecimals), computed, unavailable, HasFills: true, fx);
    }

    private static bool HasSellInWindow(IEnumerable<BrokerageTrade> fills, DateOnly? from)
        => fills.Any(f => f.Quantity < 0m && f.Price > 0m
            && (from is not { } start || DateOnly.FromDateTime(f.TradeDateTime) >= start));

    private static string Key(string provider, string symbol)
        => provider.ToLowerInvariant() + "|" + symbol.Trim().ToUpperInvariant();
}
