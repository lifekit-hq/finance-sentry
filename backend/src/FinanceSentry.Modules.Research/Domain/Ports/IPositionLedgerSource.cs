namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Cross-module port: the Asset Dossier's view of a user's ingested fills and dividend income in one
/// symbol (chart trade markers and the return decomposition). The concrete adapter lives in
/// FinanceSentry.Integration so Modules.Research never depends on Modules.BrokerageSync directly.
/// </summary>
public interface IPositionLedgerSource
{
    /// <summary>Fills and dividend income for the symbol, oldest first; empty lists when nothing was ingested.</summary>
    Task<PositionLedger> ReadForSymbolAsync(Guid userId, string symbol, CancellationToken ct = default);
}

/// <summary>Fills and dividend income for one symbol, each list oldest first.</summary>
public sealed record PositionLedger(IReadOnlyList<LedgerFill> Fills, IReadOnlyList<LedgerIncome> Income)
{
    public static PositionLedger Empty { get; } = new([], []);
}

/// <summary>
/// One fill, money native in <see cref="Currency"/>. <see cref="Quantity"/> is signed (+ bought, - sold or
/// given away); <see cref="Commission"/> is negative when paid, in <see cref="CommissionCurrency"/> when set.
/// </summary>
public sealed record LedgerFill(
    string Provider,
    DateTime TradedAt,
    decimal Quantity,
    decimal Price,
    decimal? Commission,
    string? CommissionCurrency,
    string Currency);

/// <summary>One dividend-income row, signed native: a credit positive, the tax withheld from it negative.</summary>
public sealed record LedgerIncome(string Provider, DateTime PaidAt, decimal Amount, string Currency);
