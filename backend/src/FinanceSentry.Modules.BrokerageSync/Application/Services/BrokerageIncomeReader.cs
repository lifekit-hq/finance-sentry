namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// <see cref="IBrokerageIncomeReader"/> over the ingested IBKR Flex and Inzhur cash rows, converted to USD at each
/// row's own date. Income is a dividend, a payment in lieu, a distribution or the interest received; the tax withheld
/// from a dividend is read beside it (<see cref="IncomeReceipt.IsWithholding"/>), so a figure can show it netted. An
/// Inzhur "payment" row moves a credited dividend out to the bank and is not income, and interest paid is a cost.
/// </summary>
public sealed class BrokerageIncomeReader(BrokerageSyncDbContext db, IHistoricalExchangeRateService fx) : IBrokerageIncomeReader
{
    private const string CashSymbol = "CASH";

    private static readonly HashSet<string> DividendTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dividends",
        "Payment In Lieu Of Dividends",
        "Dividend crediting",
        "Distribution crediting",
    };

    private static readonly HashSet<string> InterestTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Broker Interest Received",
        "Bond Interest Received",
    };

    private static readonly HashSet<string> WithholdingTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Withholding Tax",
        "Dividend tax",
        "Distribution tax",
    };

    public async Task<IReadOnlyList<IncomeReceipt>> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default)
    {
        var fromTime = from.ToDateTime(TimeOnly.MaxValue);
        var rows = await db.BrokerageCashTransactions.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.DateTime > fromTime && c.Amount != 0m)
            .Select(c => new { c.Symbol, c.TransactionType, c.Amount, c.Currency, c.DateTime })
            .ToListAsync(ct);

        var receipts = new List<IncomeReceipt>();
        foreach (var row in rows.OrderBy(r => r.DateTime))
        {
            var kind = DividendTypes.Contains(row.TransactionType) ? IncomeReceiptKind.Dividend
                : InterestTypes.Contains(row.TransactionType) ? IncomeReceiptKind.Interest
                : (IncomeReceiptKind?)null;
            var withholding = WithholdingTypes.Contains(row.TransactionType);
            if (kind is null && !withholding)
                continue;

            var date = DateOnly.FromDateTime(row.DateTime);
            var usd = await fx.ConvertToUsdAsync(row.Amount, row.Currency, date, ct);
            var symbol = string.IsNullOrWhiteSpace(row.Symbol) ? CashSymbol : row.Symbol.Trim().ToUpperInvariant();
            receipts.Add(new IncomeReceipt(symbol, kind ?? IncomeReceiptKind.Dividend, date, usd.Amount, withholding));
        }

        return receipts;
    }
}
