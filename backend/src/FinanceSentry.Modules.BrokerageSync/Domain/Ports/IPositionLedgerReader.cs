namespace FinanceSentry.Modules.BrokerageSync.Domain.Ports;

/// <summary>
/// Published read port: a user's ingested fills and dividend income in one symbol, for cross-module
/// readers such as Research's Asset Dossier (price-chart trade markers and the return decomposition).
/// Implemented inside BrokerageSync, which alone knows each provider's cash-row types; the
/// FinanceSentry.Integration adapter reaches BrokerageSync only through this interface.
/// </summary>
public interface IPositionLedgerReader
{
    /// <summary>
    /// The user's fills and dividend rows whose symbol matches <paramref name="symbol"/>
    /// case-insensitively, oldest first; both lists are empty when nothing was ingested.
    /// </summary>
    Task<PositionLedgerReading> ReadForSymbolAsync(Guid userId, string symbol, CancellationToken ct = default);
}

/// <summary>Fills and dividend income for one symbol, each list oldest first.</summary>
public sealed record PositionLedgerReading(
    IReadOnlyList<PositionLedgerFill> Fills,
    IReadOnlyList<PositionLedgerIncome> Income);

/// <summary>
/// One fill, money native in <see cref="Currency"/>. <see cref="Quantity"/> is signed (+ bought, - sold or
/// given away); <see cref="Commission"/> is IBKR's sign (negative = paid), in <see cref="CommissionCurrency"/>
/// when that is set.
/// </summary>
public sealed record PositionLedgerFill(
    string Provider,
    DateTime TradedAt,
    decimal Quantity,
    decimal Price,
    decimal? Commission,
    string? CommissionCurrency,
    string Currency);

/// <summary>
/// One dividend-income row, signed native in <see cref="Currency"/>: a dividend or distribution credit is
/// positive, the tax withheld from it negative.
/// </summary>
public sealed record PositionLedgerIncome(
    string Provider,
    DateTime PaidAt,
    decimal Amount,
    string Currency);
