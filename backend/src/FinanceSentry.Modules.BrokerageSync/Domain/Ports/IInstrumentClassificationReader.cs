namespace FinanceSentry.Modules.BrokerageSync.Domain.Ports;

/// <summary>
/// Published read port: the human-set tax classification of a user's broker instrument in one symbol, for
/// cross-module readers such as Research's disposal-tax context. Implemented inside BrokerageSync; the
/// FinanceSentry.Integration adapter reaches BrokerageSync only through this interface.
/// </summary>
public interface IInstrumentClassificationReader
{
    /// <summary>
    /// The classification set on the user's instrument(s) whose symbol matches <paramref name="symbol"/>
    /// case-insensitively. Null when there is no such instrument, it is unclassified, or the instruments in
    /// that symbol disagree: a regime is never guessed.
    /// </summary>
    Task<InstrumentTaxClass?> GetForSymbolAsync(Guid userId, string symbol, CancellationToken ct = default);
}

/// <summary>The tax class of an instrument as the port publishes it (the human-set classification, never inferred).</summary>
public enum InstrumentTaxClass
{
    /// <summary>An ordinary share, taxed under standard Irish CGT rules.</summary>
    OrdinaryShare = 1,

    /// <summary>An offshore fund / ETF in the gross roll-up regime (exit tax, 8-year deemed disposal).</summary>
    OffshoreFund = 2,
}
