namespace FinanceSentry.Core.Interfaces;

using FinanceSentry.Core.Utils;

/// <summary>
/// The kind of economy a <see cref="IWealthSource"/> reads (framework §5.2). It routes a source's cash into the
/// legacy per-kind slices of <see cref="BookFigures"/> (<see cref="BookFigures.BankingCashUsd"/>,
/// <see cref="BookFigures.BrokerageCashUsd"/>, <see cref="BookFigures.VenueCashUsd"/>); a kind with no slice
/// still counts in <see cref="BookFigures.CashUsd"/> and the totals.
/// </summary>
public enum EconomyKind
{
    Bank,
    Brokerage,
    CryptoVenue,
    ChainWallet,
    Fund,
    Marketplace,
    Manual,
}

/// <summary>
/// One source's contribution to the book, already in USD: its cash and its invested (non-cash) positions.
/// <see cref="FxStatus"/> is the worst conversion status among the rows it was summed from; a per-source
/// snapshot row takes its total, cash, invested value and reasons
/// (<see cref="FxStatusExtensions.ToReasons"/>) from here.
/// </summary>
public sealed record WealthSourceReading(
    decimal CashUsd,
    IReadOnlyList<BookFigurePosition> Positions,
    FxStatus FxStatus = FxStatus.Exact)
{
    public decimal InvestedValueUsd => Positions.Sum(p => p.UsdValue);

    public decimal TotalValueUsd => InvestedValueUsd + CashUsd;
}

/// <summary>
/// The ids of the three original sources, which the snapshot's sleeve columns hold and the live dashboard totals
/// from its own readers. A source registered after them is counted from its <see cref="BookSourceReading"/>.
/// </summary>
public static class WealthSourceIds
{
    public const string Banking = "banking";
    public const string Brokerage = "brokerage";
    public const string Crypto = "crypto";

    public static IReadOnlyList<string> Sleeves { get; } = [Banking, Brokerage, Crypto];
}

/// <summary>
/// A place the person holds wealth: a bank, a broker, a crypto venue. Each economy registers its sources as
/// <see cref="IWealthSource"/> services and <see cref="IBookFiguresService"/> sums every registered one, so a new
/// economy is a new registration, not a new column or a new constructor parameter.
/// </summary>
public interface IWealthSource
{
    /// <summary>
    /// Stable identifier. It is also the name recorded in <see cref="BookFigures.StaleSources"/> when the read
    /// fails, so the three original sources keep their sleeve names ("banking", "brokerage", "crypto").
    /// </summary>
    string SourceId { get; }

    EconomyKind Kind { get; }

    /// <summary>
    /// Reads the source's current figures for a person. Throws when the source is unavailable; the caller
    /// treats that source as contributing nothing and reports it stale.
    /// </summary>
    Task<WealthSourceReading> ReadAsync(Guid userId, CancellationToken ct = default);
}
