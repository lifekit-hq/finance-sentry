namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Reads the crypto sleeve's external flows per venue (fiat deposits and withdrawals, card buys and sells, transfers
/// in and out) from the venue-reported movements, classified for the crypto sleeve and valued in USD at each flow's
/// date where a dated rate exists. Runs with no person in scope, so implementations opt out of the Owner query filter
/// and keep their own user predicate.
/// </summary>
public interface ICryptoFlowReader
{
    /// <summary>
    /// Every venue linked to <paramref name="userId"/>'s crypto sleeve and its flows dated after
    /// <paramref name="from"/> (and after its own link day).
    /// </summary>
    Task<SleeveFlowReading> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default);
}
