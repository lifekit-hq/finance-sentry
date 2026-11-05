namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BrokerageSync.Domain;

/// <summary>
/// One symbol's average-cost walk over its persisted fills (issue 982). <see cref="Explained"/> is false when the
/// fills do not account for the symbol's history: a sell found nothing open to match, or the quantity the fills
/// leave open is not the quantity held, so the cost a sell released cannot be stated. <see cref="RealizedUsd"/> is the
/// gain of the sells dated on or after the walk's <c>from</c>; <see cref="SoldInWindow"/> says whether there were any.
/// </summary>
public sealed record RealizedWalkResult(bool Explained, decimal RealizedUsd, bool SoldInWindow, FxStatus Fx);

/// <summary>
/// Weighted-average cost walk, the same convention as <see cref="BrokerageCostBasisReconciler"/>: a buy costs
/// <c>quantity x price - commission</c> (a broker reports commission as a negative number, so it adds to the cost),
/// a sell releases the average cost of the units it closes and gains <c>quantity x price + commission</c> over it.
/// Native amounts are converted once per fill through <see cref="CurrencyConverter"/> (the single conversion primitive;
/// the worst conversion status is carried). A disposal with no price (an Inzhur gift) releases its cost without
/// realizing a gain.
/// </summary>
public static class RealizedPnlWalk
{
    private const decimal QuantityEpsilon = 0.0000001m;
    private const decimal HeldQuantityRelativeTolerance = 0.001m;

    /// <param name="fills">One symbol's fills at one provider, any order.</param>
    /// <param name="heldQuantity">Units held now (0 for a symbol no longer held).</param>
    /// <param name="from">Sells dated before this day are not counted; null counts them all.</param>
    public static RealizedWalkResult Walk(IEnumerable<BrokerageTrade> fills, decimal heldQuantity, DateOnly? from)
    {
        ArgumentNullException.ThrowIfNull(fills);

        var quantity = 0m;
        var costUsd = 0m;
        var realizedUsd = 0m;
        var soldInWindow = false;
        var explained = true;
        var fx = FxStatus.Exact;

        foreach (var fill in fills.OrderBy(f => f.TradeDateTime).ThenBy(f => f.IbExecutionId, StringComparer.Ordinal))
        {
            var gross = CurrencyConverter.Convert(fill.Quantity * fill.Price, fill.Currency);
            var commission = fill.Commission is decimal c
                ? CurrencyConverter.Convert(c, fill.CommissionCurrency ?? fill.Currency)
                : default(ConvertResult);
            fx = fx.Worst(gross.Status);
            if (fill.Commission is not null)
            {
                fx = fx.Worst(commission.Status);
            }

            if (fill.Quantity > 0m)
            {
                costUsd += gross.Amount - commission.Amount;
                quantity += fill.Quantity;
                continue;
            }

            if (fill.Quantity >= 0m)
            {
                continue;
            }

            var sold = -fill.Quantity;
            if (quantity <= QuantityEpsilon)
            {
                explained = false;
                continue;
            }

            var matched = Math.Min(sold, quantity);
            var released = costUsd * (matched / quantity);
            if (sold - matched > QuantityEpsilon)
            {
                explained = false;
            }

            if (fill.Price > 0m)
            {
                // gross is quantity (negative) x price: the proceeds are its magnitude for the matched units.
                var proceeds = -gross.Amount * (matched / sold);
                var gain = proceeds + commission.Amount * (matched / sold) - released;
                if (from is not { } start || DateOnly.FromDateTime(fill.TradeDateTime) >= start)
                {
                    realizedUsd += gain;
                    soldInWindow = true;
                }
            }

            costUsd -= released;
            quantity -= matched;
        }

        var tolerance = Math.Max(QuantityEpsilon, Math.Abs(heldQuantity) * HeldQuantityRelativeTolerance);
        if (Math.Abs(heldQuantity - quantity) > tolerance)
        {
            explained = false;
        }

        return new RealizedWalkResult(explained, realizedUsd, soldInWindow, fx);
    }
}
