namespace FinanceSentry.Core.Interfaces;

using System.Text.Json.Serialization;
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
/// How a value was established (framework §5.3 step 3; GIPS 2.B.6, IFRS 13 levels). Ordered best to worst, so the
/// worst of several is the maximum. <see cref="L3Estimated"/> makes the figure ESTIMATED (<c>ESTIMATED_VALUE</c>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Reliability>))]
public enum Reliability
{
    /// <summary>A quoted price for the identical asset in an active market.</summary>
    L1QuotedIdentical,

    /// <summary>Observable but not an identical active quote (a thin market, a fund's published unit price).</summary>
    L2Observable,

    /// <summary>An estimate: a value the person entered, or a model.</summary>
    L3Estimated,
}

/// <summary>
/// Whether a holding can become withdrawable money (framework §5.3 step 4), a separate axis from its price. Total
/// net worth counts every convertibility; liquid net worth ("of which liquid", C5) counts <see cref="Cash"/> only.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Convertibility>))]
public enum Convertibility
{
    /// <summary>Withdrawable within days at about the quoted price.</summary>
    Cash,

    /// <summary>Spendable only inside its own economy (a marketplace wallet that cannot be withdrawn).</summary>
    InEconomy,

    /// <summary>Convertible later but not now (a lock-up, a trade hold, a redemption notice period).</summary>
    Locked,
}

/// <summary>
/// One source's contribution to the book, already in USD: its cash and its invested (non-cash) positions.
/// <see cref="FxStatus"/> is the worst conversion status among the rows it was summed from; a per-source
/// snapshot row takes its total, cash, invested value and reasons
/// (<see cref="FxStatusExtensions.ToReasons"/>) from here. <see cref="Reliability"/> is the worst reliability among
/// the values it holds; an L3 reading makes every figure it is summed into ESTIMATED.
/// </summary>
public sealed record WealthSourceReading(
    decimal CashUsd,
    IReadOnlyList<BookFigurePosition> Positions,
    FxStatus FxStatus = FxStatus.Exact,
    Reliability Reliability = Reliability.L1QuotedIdentical)
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

    /// <summary>The values the person enters by hand (<c>ManualWealthSource</c>, Wealth module); not a sleeve.</summary>
    public const string Manual = "manual";

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
