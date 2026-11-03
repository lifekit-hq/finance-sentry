namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Core.Interfaces;

/// <summary>
/// Spots a brokerage feed whose quotes have stopped moving: a liquid symbol whose per-share price on a weekday is
/// the same as at its latest capture on an earlier weekday cannot be a live quote (a price that stays put across two
/// trading days is a feed replaying its last answer). Weekends are never judged - the market is shut, so an unchanged
/// price is the truth.
/// <para>
/// A symbol is not judged when it is not an exchange quote at all (idle cash, REIT units and bonds priced off a
/// statement, the Inzhur platform) or when it is illiquid by its own record: if, before the current run of unchanged
/// days, at least <see cref="IlliquidShare"/> of its weekday-to-weekday moves were unchanged (over at least
/// <see cref="MinHistoryMoves"/> moves), a thinly traded ETF that sits on one price for days is normal for it. A
/// symbol with less history than that is treated as liquid, so a new holding can raise a flag on a coincidence; the
/// cost of a false flag is a stale marker on the day, never a changed figure. The day after a market holiday also
/// reads as unchanged for every symbol, for the same reason.
/// </para>
/// </summary>
public static class FrozenQuoteDetector
{
    /// <summary>How far back price history is read: enough weekday moves to tell an illiquid symbol from a liquid one.</summary>
    public const int LookbackDays = 60;

    private const int MinHistoryMoves = 8;
    private const decimal IlliquidShare = 0.25m;
    private const decimal SamePriceTolerance = 0.000001m;
    private const string InzhurProvider = "inzhur";

    private static readonly HashSet<string> UnquotedInstrumentTypes = new(StringComparer.OrdinalIgnoreCase) { "CASH", "REIT", "BOND" };

    public static bool HasFrozenLiquidQuote(
        IReadOnlyList<BrokerageHoldingSummary> holdings, IReadOnlyList<DailySymbolPrice> history, DateOnly today)
    {
        if (!IsWeekday(today))
            return false;

        var pricesBySymbol = history
            .Where(p => IsWeekday(p.Date) && p.Date < today && p.PricePerShare > 0m)
            .GroupBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Date).Select(p => p.PricePerShare).ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var holding in holdings)
        {
            if (holding.Quantity <= 0m
                || UnquotedInstrumentTypes.Contains(holding.InstrumentType)
                || string.Equals(holding.Provider, InzhurProvider, StringComparison.OrdinalIgnoreCase)
                || !pricesBySymbol.TryGetValue(holding.Symbol, out var earlier))
                continue;

            var price = holding.UsdValue / holding.Quantity;
            if (price <= 0m || !SamePrice(price, earlier[^1]))
                continue;

            if (!IsHabituallyUnchanged([.. earlier, price]))
                return true;
        }

        return false;
    }

    // The prices run oldest to newest and end with the one under test, which equals the one before it. The unchanged
    // streak that ends here is left out of the record, so a feed that has been frozen for days is not excused by
    // its own freeze.
    private static bool IsHabituallyUnchanged(List<decimal> prices)
    {
        var moves = new List<bool>();
        for (var i = 1; i < prices.Count; i++)
            moves.Add(SamePrice(prices[i], prices[i - 1]));

        var streak = 0;
        while (streak < moves.Count && moves[moves.Count - 1 - streak])
            streak++;

        var record = moves.Take(moves.Count - streak).ToList();
        return record.Count >= MinHistoryMoves && (decimal)record.Count(same => same) / record.Count >= IlliquidShare;
    }

    private static bool SamePrice(decimal a, decimal b)
        => Math.Abs(a - b) <= Math.Max(a, b) * SamePriceTolerance;

    private static bool IsWeekday(DateOnly date)
        => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
