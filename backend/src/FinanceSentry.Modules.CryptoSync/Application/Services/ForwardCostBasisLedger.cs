using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;

namespace FinanceSentry.Modules.CryptoSync.Application.Services;

/// <summary>
/// Where a forward ledger stands for one holding: the known-cost quantity and its USD cost, the
/// quantity whose cost is unknown, and what the fills so far realized.
/// </summary>
public sealed record ForwardLedgerState(
    decimal TrackedQuantity,
    decimal TrackedCostUsd,
    decimal UntrackedQuantity,
    decimal RealizedPnlUsd,
    DateTime? LastTradeAt,
    int TradeCount)
{
    public static ForwardLedgerState Empty { get; } = new(0m, 0m, 0m, 0m, null, 0);

    /// <summary>
    /// Cost basis of the whole position — known only while no unpriced lot is held. Null while any
    /// is, never a guess (#472).
    /// </summary>
    public decimal? CostBasisUsd =>
        UntrackedQuantity == 0m && TrackedQuantity > 0m ? Math.Round(TrackedCostUsd, 4) : null;

    public decimal? AverageBuyPriceUsd =>
        CostBasisUsd is null ? null : Math.Round(TrackedCostUsd / TrackedQuantity, 8);
}

/// <summary>
/// Weighted-average cost basis accumulated forward from the connect date, for a venue that cannot
/// serve older fills (Revolut X, #472). <see cref="CostBasisCalculator"/> assumes the full history;
/// this ledger instead carries the lots it never saw a price for as
/// <see cref="ForwardLedgerState.UntrackedQuantity"/>:
///
/// <list type="bullet">
/// <item><b>Transfers.</b> Whatever the fills do not explain — the position held at connect, a
/// deposit, a fill on a pair that cannot be priced in USD — is untracked quantity. It is taken to
/// have arrived before this walk's fills, so a sell in the same walk never realizes P&amp;L
/// against a cost it does not know. A shortfall (a withdrawal, a fee taken in the asset) leaves at
/// the end, pro rata.</item>
/// <item><b>Sells.</b> With no untracked quantity a sell realizes P&amp;L at the average cost.
/// With some, the average is unknown: the sell shrinks tracked and untracked quantities in
/// proportion — what weighted average does — and realizes nothing. Once the position is closed
/// the unknown lots are gone and the next buy starts clean.</item>
/// <item><b>Cost basis.</b> Known only while untracked quantity is zero; null otherwise.</item>
/// </list>
///
/// <c>reconcile</c> is false when the walk stopped short of the balance snapshot: the
/// fills are applied but transfers are left for the walk that catches up, which sees them all.
/// </summary>
public sealed class ForwardCostBasisLedger
{
    // Quantities are stored at 10 decimal places; anything under this is rounding, not a lot.
    private const decimal QuantityEpsilon = 0.00000001m;

    public ForwardLedgerState Apply(
        ForwardLedgerState state,
        IEnumerable<CryptoTrade> trades,
        decimal currentQuantity,
        bool reconcile)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(trades);

        // Stable sort: fills sharing a timestamp keep the order the adapter returned them in.
        var fills = trades.OrderBy(t => t.Timestamp).ToList();

        var tracked = state.TrackedQuantity;
        var cost = state.TrackedCostUsd;
        var untracked = state.UntrackedQuantity;
        var realized = state.RealizedPnlUsd;
        var lastAt = state.LastTradeAt;
        var count = state.TradeCount;

        if (reconcile)
        {
            var netFilled = fills.Sum(t => t.IsBuyer ? t.Quantity : -t.Quantity);
            var arrived = currentQuantity - (tracked + untracked + netFilled);
            if (arrived > QuantityEpsilon)
            {
                untracked += arrived;
            }
        }

        foreach (var fill in fills)
        {
            count++;
            lastAt = fill.Timestamp;

            if (fill.IsBuyer)
            {
                tracked += fill.Quantity;
                cost += fill.QuoteQuantityUsd;
                continue;
            }

            var held = tracked + untracked;
            if (held <= QuantityEpsilon)
            {
                // Nothing the ledger knows of is left to sell.
                continue;
            }

            var sold = Math.Min(fill.Quantity, held);
            if (untracked <= QuantityEpsilon)
            {
                var average = cost / tracked;
                realized += (fill.PriceUsd - average) * sold;
                cost -= average * sold;
                tracked -= sold;
            }
            else
            {
                var kept = (held - sold) / held;
                tracked *= kept;
                cost *= kept;
                untracked *= kept;
            }

            if (tracked + untracked <= QuantityEpsilon)
            {
                (tracked, cost, untracked) = (0m, 0m, 0m);
            }
        }

        if (reconcile)
        {
            var held = tracked + untracked;
            var gap = currentQuantity - held;
            if (gap < -QuantityEpsilon)
            {
                var kept = held > 0m ? Math.Max(currentQuantity, 0m) / held : 0m;
                tracked *= kept;
                cost *= kept;
                untracked *= kept;
            }
            else if (gap > QuantityEpsilon)
            {
                // Defensive: the arrivals above already cover the balance, but a shortfall is unpriced.
                untracked += gap;
            }
        }

        if (untracked <= QuantityEpsilon)
        {
            untracked = 0m;
        }

        if (tracked <= QuantityEpsilon)
        {
            (tracked, cost) = (0m, 0m);
        }

        return new ForwardLedgerState(
            TrackedQuantity: Math.Round(tracked, 10),
            TrackedCostUsd: Math.Round(cost, 10),
            UntrackedQuantity: Math.Round(untracked, 10),
            RealizedPnlUsd: Math.Round(realized, 4),
            LastTradeAt: lastAt,
            TradeCount: count);
    }
}
