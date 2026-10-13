namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

using FinanceSentry.Modules.BrokerageSync.Domain.Ports;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

/// <summary>
/// <see cref="IPositionLedgerReader"/> over the ingested IBKR Flex and Inzhur ledgers. Dividend income is
/// the rows whose provider type is a dividend, a payment in lieu of one, a distribution, or the tax withheld
/// from any of those; an Inzhur "payment" row moves a credited dividend out to the bank and is not income.
/// </summary>
public sealed class PositionLedgerReader(
    IBrokerageTradeRepository trades,
    IBrokerageCashTransactionRepository cashTransactions) : IPositionLedgerReader
{
    // IBKR Flex CashTransaction types and the Inzhur cabinet's transaction types (InzhurLedgerMapper).
    private static readonly HashSet<string> IncomeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dividends",
        "Payment In Lieu Of Dividends",
        "Withholding Tax",
        "Dividend crediting",
        "Distribution crediting",
        "Dividend tax",
        "Distribution tax",
    };

    public async Task<PositionLedgerReading> ReadForSymbolAsync(
        Guid userId, string symbol, CancellationToken ct = default)
    {
        var fills = await trades.GetBySymbolUnscopedAsync(userId, symbol, ct);
        var cash = await cashTransactions.GetBySymbolUnscopedAsync(userId, symbol, ct);

        return new PositionLedgerReading(
            fills
                .Where(t => t.Quantity != 0m)
                .OrderBy(t => t.TradeDateTime)
                .Select(t => new PositionLedgerFill(
                    t.Provider, t.TradeDateTime, t.Quantity, t.Price, t.Commission, t.CommissionCurrency, t.Currency))
                .ToList(),
            cash
                .Where(c => IncomeTypes.Contains(c.TransactionType) && c.Amount != 0m)
                .OrderBy(c => c.DateTime)
                .Select(c => new PositionLedgerIncome(c.Provider, c.DateTime, c.Amount, c.Currency))
                .ToList());
    }
}
