namespace FinanceSentry.Core.Interfaces;

using FinanceSentry.Core.Utils;

/// <summary>
/// Why a symbol's realized P&amp;L is withheld: the fills do not explain its history, so the average cost a sell
/// released cannot be stated (<c>INCOMPLETE_HISTORY</c>: a sell with nothing open to match, or a held quantity the
/// fills do not reach).
/// </summary>
public sealed record UnavailableRealizedSymbol(string Provider, string Symbol);

/// <summary>
/// The realized gain the brokerage sleeve's persisted fills lock in (average cost, commissions in), summed over the
/// sells dated on or after the reading's <c>from</c>, in USD. Only symbols whose fills explain their whole history
/// are summed; a symbol that sold in the window but whose history the fills do not explain is listed in
/// <see cref="UnavailableSymbols"/> and left out, never estimated. <see cref="HasFills"/> says whether any fill is
/// ingested at all (no fills is "not measurable", not a zero).
/// </summary>
public sealed record RealizedPnlReading(
    decimal Usd,
    int SymbolsComputed,
    IReadOnlyList<UnavailableRealizedSymbol> UnavailableSymbols,
    bool HasFills,
    FxStatus Fx);

/// <summary>
/// The unrealized gain over the open positions whose cost basis the fills verify (fs-688), with what it covers:
/// <see cref="MeasuredValueUsd"/> of <see cref="TotalValueUsd"/> (cash rows excluded) sits in
/// <see cref="PositionsMeasured"/> of <see cref="PositionsTotal"/> positions. <see cref="UnverifiedSymbols"/> are
/// the positions whose basis state is not Verified: their gain is not stated.
/// </summary>
public sealed record UnrealizedPnlReading(
    decimal Usd,
    decimal CostBasisUsd,
    decimal MeasuredValueUsd,
    decimal TotalValueUsd,
    int PositionsMeasured,
    int PositionsTotal,
    IReadOnlyList<string> UnverifiedSymbols);

public sealed record BrokeragePnlReading(RealizedPnlReading Realized, UnrealizedPnlReading Unrealized);

/// <summary>
/// Reads the brokerage sleeve's realized and unrealized P&amp;L from the persisted fills and the holdings' verified
/// cost basis (issue 982). Runs with no person in scope, so implementations opt out of the Owner query filter and
/// keep their own user predicate.
/// </summary>
public interface IBrokeragePnlReader
{
    /// <param name="userId">The person whose sleeve is read.</param>
    /// <param name="realizedFrom">Sells dated before this day are not counted; null counts every ingested sell.</param>
    Task<BrokeragePnlReading> ReadAsync(Guid userId, DateOnly? realizedFrom, CancellationToken ct = default);
}
