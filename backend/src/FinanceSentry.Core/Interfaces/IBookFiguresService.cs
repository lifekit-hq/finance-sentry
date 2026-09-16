namespace FinanceSentry.Core.Interfaces;

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
public sealed record BookFigures(
    decimal CashUsd,
    decimal BankingCashUsd,
    decimal BrokerageCashUsd,
    decimal InvestedValueUsd,
    decimal TotalValueUsd,
    IReadOnlyList<BookFigurePosition> Positions,
    bool IsStale,
    IReadOnlyList<string> StaleSources,
    decimal VenueCashUsd = 0m)
{
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
