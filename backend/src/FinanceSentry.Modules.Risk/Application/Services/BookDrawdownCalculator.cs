namespace FinanceSentry.Modules.Risk.Application.Services;

using FinanceSentry.Modules.Risk.Domain;

/// <summary>
/// Measures the book's current decline from its peak over a window of holding snapshots (#700),
/// neutral to money moving in or out. The naive measure - peak value against current value - reads a
/// withdrawal as a loss, so it is not used. Instead each step between two captures revalues the
/// quantities held at the EARLIER capture at the LATER capture's prices; the ratio of that to the
/// earlier value is the step's return, and the returns chain into a growth index. A position bought
/// between captures contributes nothing to the step that bought it, and one sold simply stops
/// contributing. The drawdown is the index's fall from its highest point in the window.
/// Pure: no I/O, no clock.
/// </summary>
public static class BookDrawdownCalculator
{
    // A position whose quantity moved this much while its value did not is a split or reorganisation,
    // not a market move: the price changed because the unit did.
    private const decimal SplitValueTolerance = 0.02m;
    private const decimal SplitQuantityChange = 0.10m;
    private const int DrawdownDecimals = 4;

    /// <summary>
    /// The current drawdown as a fraction of the peak, or null when fewer than two captures are
    /// available to step between. <paramref name="history"/> is every snapshot in the window;
    /// <paramref name="live"/> is appended as the latest capture.
    /// </summary>
    public static decimal? Measure(IReadOnlyList<HoldingSnapshot> history, IReadOnlyList<BookPosition> live)
    {
        var captures = history
            .GroupBy(h => h.CapturedAt)
            .OrderBy(g => g.Key)
            .Select(g => ToCapture(g.Select(h => (h.Symbol, h.Sleeve, h.Quantity, h.UsdValue))))
            .ToList();
        captures.Add(ToCapture(live.Select(p => (p.Symbol, p.Sleeve, p.Quantity, p.UsdValue))));

        var index = 1m;
        var peak = 1m;
        var steps = 0;
        for (var i = 1; i < captures.Count; i++)
        {
            if (Step(captures[i - 1], captures[i]) is not { } stepReturn)
                continue;

            index *= 1m + stepReturn;
            peak = Math.Max(peak, index);
            steps++;
        }

        return steps == 0 ? null : Math.Round(1m - (index / peak), DrawdownDecimals);
    }

    private static decimal? Step(Dictionary<string, Holding> earlier, Dictionary<string, Holding> later)
    {
        var start = 0m;
        var end = 0m;
        foreach (var (key, before) in earlier)
        {
            start += before.UsdValue;
            end += later.TryGetValue(key, out var after) && !IsReorganisation(before, after)
                ? before.Quantity * (after.UsdValue / after.Quantity)
                : before.UsdValue;
        }

        return start > 0m ? (end / start) - 1m : null;
    }

    private static bool IsReorganisation(Holding before, Holding after)
        => Math.Abs((after.UsdValue / before.UsdValue) - 1m) < SplitValueTolerance
            && Math.Abs((after.Quantity / before.Quantity) - 1m) > SplitQuantityChange;

    private static Dictionary<string, Holding> ToCapture(
        IEnumerable<(string Symbol, string Sleeve, decimal Quantity, decimal UsdValue)> positions)
    {
        var capture = new Dictionary<string, Holding>(StringComparer.OrdinalIgnoreCase);
        foreach (var (symbol, sleeve, quantity, usdValue) in positions)
        {
            if (quantity <= 0m || usdValue <= 0m)
                continue;

            var key = $"{sleeve}:{symbol}";
            capture[key] = capture.TryGetValue(key, out var existing)
                ? new Holding(existing.Quantity + quantity, existing.UsdValue + usdValue)
                : new Holding(quantity, usdValue);
        }

        return capture;
    }

    private readonly record struct Holding(decimal Quantity, decimal UsdValue);
}
