namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// <see cref="IBrokerageCostReader"/> over the ingested IBKR Flex and Inzhur rows, converted to USD at each row's own
/// date. A trade's commission is a <see cref="CostKind.Commission"/> (the commission on a currency-pair trade, whose
/// instrument is CASH, is the cost of converting currency) and its transaction taxes a <see cref="CostKind.Fee"/>; the
/// cash rows add the broker's fee rows, Inzhur's sale commission and conversion surcharge, and the tax withheld from
/// income. Flex books money paid as a negative amount, so a cost is the amount's negation and a refund reduces it.
/// </summary>
public sealed class BrokerageCostReader(BrokerageSyncDbContext db, IHistoricalExchangeRateService fx) : IBrokerageCostReader
{
    private const string CashInstrumentType = "CASH";

    private static readonly Dictionary<string, CostKind> CashCosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Other Fees"] = CostKind.Fee,
        ["Broker Fees"] = CostKind.Fee,
        ["Advisor Fees"] = CostKind.Fee,
        ["Commission Adjustments"] = CostKind.Commission,
        ["Sale commission"] = CostKind.Commission,
        ["Sale tax"] = CostKind.Fee,
        ["Conversion Surcharge"] = CostKind.FxConversion,
        ["Withholding Tax"] = CostKind.Withholding,
        ["871(m) Withholding"] = CostKind.Withholding,
        ["Dividend tax"] = CostKind.Withholding,
        ["Distribution tax"] = CostKind.Withholding,
        ["Bonuses tax"] = CostKind.Withholding,
    };

    public async Task<CostReading> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default)
    {
        var fromTime = from.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        var items = new List<(DateTime At, CostItem Item)>();

        var pairInstrumentIds = (await db.BrokerageInstruments.IgnoreQueryFilters([OwnerQueryFilter.Name])
                .AsNoTracking()
                .Where(i => i.UserId == userId && i.InstrumentType == CashInstrumentType)
                .Select(i => i.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var rawTrades = await db.BrokerageTrades.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.TradeDateTime > fromTime && (t.Commission != null || t.Taxes != null))
            .Select(t => new { t.Commission, t.CommissionCurrency, t.Taxes, t.Currency, t.TradeDateTime, t.InstrumentId })
            .ToListAsync(ct);
        var trades = rawTrades.Select(t => new
        {
            t.Commission,
            t.CommissionCurrency,
            t.Taxes,
            t.Currency,
            t.TradeDateTime,
            IsCurrencyPair = t.InstrumentId is { } id && pairInstrumentIds.Contains(id),
        });

        foreach (var trade in trades)
        {
            var date = DateOnly.FromDateTime(trade.TradeDateTime);
            if (trade.Commission is { } commission and not 0m)
            {
                var currency = string.IsNullOrWhiteSpace(trade.CommissionCurrency) ? trade.Currency : trade.CommissionCurrency;
                var usd = await fx.ConvertToUsdAsync(-commission, currency, date, ct);
                items.Add((trade.TradeDateTime,
                    new CostItem(trade.IsCurrencyPair ? CostKind.FxConversion : CostKind.Commission, date, usd.Amount, usd.Status)));
            }

            if (trade.Taxes is { } taxes and not 0m)
            {
                var usd = await fx.ConvertToUsdAsync(-taxes, trade.Currency, date, ct);
                items.Add((trade.TradeDateTime, new CostItem(CostKind.Fee, date, usd.Amount, usd.Status)));
            }
        }

        var rows = await db.BrokerageCashTransactions.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.DateTime > fromTime && c.Amount != 0m)
            .Select(c => new { c.TransactionType, c.Amount, c.Currency, c.DateTime })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            if (!CashCosts.TryGetValue(row.TransactionType, out var kind))
                continue;

            var date = DateOnly.FromDateTime(row.DateTime);
            var usd = await fx.ConvertToUsdAsync(-row.Amount, row.Currency, date, ct);
            items.Add((row.DateTime, new CostItem(kind, date, usd.Amount, usd.Status)));
        }

        return new CostReading([.. items.OrderBy(i => i.At).Select(i => i.Item)], 0);
    }
}
