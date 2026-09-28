namespace FinanceSentry.Modules.BrokerageSync.Domain.Ports;

/// <summary>
/// Published read port (#673): a user's IBKR tax lots in one symbol, for cross-module readers such
/// as Research's Asset Dossier. Implemented inside BrokerageSync; the FinanceSentry.Integration
/// adapter reaches BrokerageSync only through this interface.
/// </summary>
public interface ITaxLotReader
{
    /// <summary>
    /// The user's tax lots whose symbol matches <paramref name="symbol"/> case-insensitively; empty
    /// when the user holds no lots in that symbol.
    /// </summary>
    Task<IReadOnlyList<TaxLotReading>> ListForSymbolAsync(
        Guid userId, string symbol, CancellationToken ct = default);
}

/// <summary>
/// One tax lot as a cross-module reader sees it. The cost-basis and gain/loss fields are null
/// whenever the lot's basis is not verified.
/// </summary>
public sealed record TaxLotReading(
    decimal Quantity,
    decimal CurrentValueUsd,
    decimal? AverageCostUsd,
    decimal? CostBasisUsd,
    decimal? UnrealizedPnlUsd,
    decimal? UnrealizedPnlPercent,
    DateTime? AcquiredAt,
    bool IsLongTerm);
