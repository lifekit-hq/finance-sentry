using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BrokerageSync.Domain;

namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

/// <summary>
/// Where a holding's cost basis stands relative to what its persisted fills actually support (fs-688).
/// </summary>
public enum BasisState
{
    /// <summary>No fill covers some (or all) of the held quantity — the acquisition history is incomplete,
    /// so no cost basis or gain/loss can be trusted.</summary>
    Unknown = 0,

    /// <summary>Fills cover the full held quantity but the recomputed cost basis disagrees with the
    /// stored one beyond tolerance — the stored figure may be corrupted.</summary>
    Unverified = 1,

    /// <summary>Fills cover the full held quantity and the recomputed cost basis agrees with the stored one.</summary>
    Verified = 2,
}

public sealed record BasisReconciliationResult(
    BasisState State,
    decimal? RecomputedCostBasisUsd,
    decimal RecomputedQuantity,
    decimal UntrackedQuantity);

/// <summary>
/// Recomputes a brokerage holding's cost basis independently from its persisted <see cref="BrokerageTrade"/>
/// fills — weighted-average, native-currency amounts converted at this reader boundary via
/// <see cref="CurrencyConverter"/> — and flags whether the stored basis can be trusted (fs-688).
///
/// Deliberately ignores the trade-level <c>CostBasis</c>/<c>RealizedPnl</c> fields IBKR reports per fill:
/// those are exactly what this check exists to verify, so recomputation starts from raw quantity/price/
/// commission instead.
/// </summary>
public sealed class BrokerageCostBasisReconciler
{
    // Quantities are stored at high precision; anything under this is rounding, not a real lot.
    private const decimal QuantityEpsilon = 0.0000001m;

    private const decimal CostBasisAbsoluteToleranceUsd = 1.00m;
    private const decimal CostBasisRelativeTolerance = 0.02m;

    public BasisReconciliationResult Reconcile(BrokerageHolding holding, IReadOnlyList<BrokerageTrade> allUserTrades)
    {
        ArgumentNullException.ThrowIfNull(holding);
        ArgumentNullException.ThrowIfNull(allUserTrades);

        var fills = allUserTrades
            .Where(t => Matches(holding, t))
            .OrderBy(t => t.TradeDateTime)
            .ToList();

        if (fills.Count == 0)
        {
            return new BasisReconciliationResult(BasisState.Unknown, null, 0m, holding.Quantity);
        }

        decimal runningQty = 0m;
        decimal runningCostUsd = 0m;

        foreach (var trade in fills)
        {
            if (trade.Quantity > 0m)
            {
                var costNative = (trade.Quantity * trade.Price) - (trade.Commission ?? 0m);
                runningCostUsd += CurrencyConverter.ToUsd(costNative, trade.Currency);
                runningQty += trade.Quantity;
            }
            else if (trade.Quantity < 0m)
            {
                if (runningQty <= 0m)
                    continue;

                var sellQty = Math.Min(-trade.Quantity, runningQty);
                var averageCost = runningCostUsd / runningQty;
                runningCostUsd -= averageCost * sellQty;
                runningQty -= sellQty;
            }
        }

        var untrackedQuantity = holding.Quantity - runningQty;
        var quantityTolerance = Math.Max(QuantityEpsilon, Math.Abs(holding.Quantity) * 0.001m);

        if (untrackedQuantity > quantityTolerance)
        {
            // Holding carries more quantity than the fills explain — some of it arrived without a
            // recorded acquisition (a transfer-in, or history predating persistence).
            return new BasisReconciliationResult(
                BasisState.Unknown,
                Math.Round(runningCostUsd, 4),
                Math.Round(runningQty, 8),
                Math.Round(untrackedQuantity, 8));
        }

        var recomputed = Math.Round(runningCostUsd, 4);

        if (holding.CostBasisUsd is not decimal stored)
        {
            return new BasisReconciliationResult(BasisState.Unknown, recomputed, Math.Round(runningQty, 8), 0m);
        }

        var diff = Math.Abs(recomputed - stored);
        var tolerance = Math.Max(CostBasisAbsoluteToleranceUsd, Math.Abs(stored) * CostBasisRelativeTolerance);
        var state = diff <= tolerance ? BasisState.Verified : BasisState.Unverified;

        return new BasisReconciliationResult(state, recomputed, Math.Round(runningQty, 8), 0m);
    }

    private static bool Matches(BrokerageHolding holding, BrokerageTrade trade)
    {
        if (trade.UserId != holding.UserId)
            return false;

        if (holding.InstrumentId is Guid instrumentId && trade.InstrumentId is Guid tradeInstrumentId)
            return instrumentId == tradeInstrumentId;

        return string.Equals(trade.Symbol, holding.Symbol, StringComparison.OrdinalIgnoreCase);
    }
}
