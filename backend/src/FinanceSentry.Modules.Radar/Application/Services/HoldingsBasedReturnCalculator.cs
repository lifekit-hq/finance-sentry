using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;

namespace FinanceSentry.Modules.Radar.Application.Services;

/// <summary>
/// A holdings-based price return for the book over a window of captures (finance-framework R1(b)).
/// Same method as the Risk module's <c>BookDrawdownCalculator</c>: each step between two captures
/// revalues the quantities held at the EARLIER capture at the LATER capture's prices, the ratio of that
/// to the earlier value is the step's return, and the step returns chain into one growth index. A
/// deposit, or a purchase made from idle cash, adds a position the earlier capture did not hold, so it
/// contributes nothing to the step that bought it. A position sold between captures is carried at its
/// earlier value for that step (its exit price is not in a capture) and contributes nothing after.
/// It is a price return: dividends paid to cash are outside it, so compare it with SPY's price return.
/// Pure: no I/O, no clock.
/// </summary>
public static class HoldingsBasedReturnCalculator
{
    // A position whose quantity moved this much while its value did not is a split or reorganisation,
    // not a market move: the price changed because the unit did.
    private const decimal SplitValueTolerance = 0.02m;
    private const decimal SplitQuantityChange = 0.10m;
    private const int ReturnDecimals = 4;

    /// <summary>
    /// The chained return across <paramref name="captures"/>, which must already be the captures inside the
    /// window; they are ordered here. Unavailable when fewer than two are given, when a held position lacks a
    /// price at either end of a step, or when no step had a priced position to revalue.
    /// </summary>
    public static HoldingsBasedReturnResult Measure(IReadOnlyList<HoldingsCapture> captures)
    {
        var ordered = captures
            .OrderBy(c => c.CapturedAt)
            .Select(c => Aggregate(c.Holdings))
            .ToList();

        if (ordered.Count < 2)
        {
            return HoldingsBasedReturnResult.Unavailable(HoldingsReturnUnavailableReason.InsufficientCaptures);
        }

        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 1m;
        var steps = 0;
        for (var i = 1; i < ordered.Count; i++)
        {
            var step = Step(ordered[i - 1], ordered[i], missing);
            if (step is null)
            {
                continue;
            }

            index *= 1m + step.Value;
            steps++;
        }

        if (missing.Count > 0)
        {
            return HoldingsBasedReturnResult.Unavailable(HoldingsReturnUnavailableReason.MissingPrice, [.. missing]);
        }

        return steps == 0
            ? HoldingsBasedReturnResult.Unavailable(HoldingsReturnUnavailableReason.NoInvestedBase)
            : HoldingsBasedReturnResult.Of(Math.Round(index - 1m, ReturnDecimals));
    }

    private static decimal? Step(
        Dictionary<string, Position> earlier, Dictionary<string, Position> later, SortedSet<string> missing)
    {
        var start = 0m;
        var end = 0m;
        var unpriced = false;
        foreach (var (symbol, before) in earlier)
        {
            if (!before.Priced)
            {
                missing.Add(symbol);
                unpriced = true;
                continue;
            }

            start += before.UsdValue;
            if (!later.TryGetValue(symbol, out var after))
            {
                end += before.UsdValue;
            }
            else if (!after.Priced)
            {
                missing.Add(symbol);
                unpriced = true;
            }
            else
            {
                end += IsReorganisation(before, after)
                    ? before.UsdValue
                    : before.Quantity * (after.UsdValue / after.Quantity);
            }
        }

        return unpriced || start <= 0m ? null : (end / start) - 1m;
    }

    private static bool IsReorganisation(Position before, Position after)
        => Math.Abs((after.UsdValue / before.UsdValue) - 1m) < SplitValueTolerance
            && Math.Abs((after.Quantity / before.Quantity) - 1m) > SplitQuantityChange;

    // One entry per symbol (lots of the same symbol are summed); a symbol is Priced only when every row
    // held at that capture carries a value. A row with no quantity is not held and is left out.
    private static Dictionary<string, Position> Aggregate(IReadOnlyList<CapturedHolding> holdings)
    {
        var capture = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase);
        foreach (var holding in holdings)
        {
            if (holding.Quantity <= 0m)
            {
                continue;
            }

            var priced = holding.UsdValue > 0m;
            var value = priced ? holding.UsdValue : 0m;
            capture[holding.Symbol] = capture.TryGetValue(holding.Symbol, out var existing)
                ? new Position(existing.Quantity + holding.Quantity, existing.UsdValue + value, existing.Priced && priced)
                : new Position(holding.Quantity, value, priced);
        }

        return capture;
    }

    private readonly record struct Position(decimal Quantity, decimal UsdValue, bool Priced);
}
