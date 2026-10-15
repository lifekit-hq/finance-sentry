namespace FinanceSentry.Core.Interfaces;

using FinanceSentry.Core.Utils;

/// <summary>
/// A single position in the invested (non-cash) portion of the book, already normalized
/// to a canonical asset class by <see cref="Domain.AssetClassNormalizer"/>.
/// </summary>
public sealed record BookFigurePosition(
    string Symbol,
    string AssetClass,
    decimal Quantity,
    decimal? CostBasisUsd,
    decimal UsdValue,
    string Provider);

/// <summary>
/// The canonical book snapshot — one source of truth for cash, invested value, and totals.
/// Produced by <see cref="IBookFiguresService"/> and consumed by all three surfaces
/// (portfolio snapshot, allocation-drift, and risk compliance) so they are guaranteed
/// to agree on cashUsd / investedValueUsd / totalValueUsd.
/// <see cref="CashUsd"/> is <see cref="BankingCashUsd"/> + <see cref="BrokerageCashUsd"/> +
/// <see cref="VenueCashUsd"/>.
/// </summary>
/// <param name="VenueCashUsd">
/// Fiat held on crypto venues (Revolut X EUR/USD balances, #472) — cash, but not bank cash.
/// </param>
/// <param name="FxStatus">
/// The worst conversion status among the holdings and accounts the figures were summed from
/// (<see cref="Utils.FxStatus"/>): <see cref="FxStatusExtensions.ToReasons"/> turns it into the
/// FX_TODAY_RATE / FX_UNKNOWN_1TO1 reason codes. A source that failed to read contributes nothing.
/// </param>
public sealed record BookFigures(
    decimal CashUsd,
    decimal BankingCashUsd,
    decimal BrokerageCashUsd,
    decimal InvestedValueUsd,
    decimal TotalValueUsd,
    IReadOnlyList<BookFigurePosition> Positions,
    bool IsStale,
    IReadOnlyList<string> StaleSources,
    decimal VenueCashUsd = 0m,
    FxStatus FxStatus = FxStatus.Exact)
{
    /// <summary>The FX reason codes the figures carry; empty when every conversion used a live rate.</summary>
    public IReadOnlyList<string> FxReasonCodes => FxStatus.ToReasons();

    public static BookFigures Empty { get; } =
        new(0m, 0m, 0m, 0m, 0m, [], false, []);
}

/// <summary>
/// Aggregates banking, brokerage, and crypto sources into a <see cref="BookFigures"/>
/// snapshot. Idle brokerage cash (instrument type "CASH") is bucketed into
/// <see cref="BookFigures.BrokerageCashUsd"/>, and fiat held on crypto venues into
/// <see cref="BookFigures.VenueCashUsd"/>, not into the invested positions list —
/// the same convention used by the allocation-drift tool. Each source degrades
/// gracefully: a failed read contributes zero and is recorded in
/// <see cref="BookFigures.StaleSources"/>.
/// </summary>
public interface IBookFiguresService
{
    Task<BookFigures> ReadAsync(Guid userId, CancellationToken ct = default);
}
