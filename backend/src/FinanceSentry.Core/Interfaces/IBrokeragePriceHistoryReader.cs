namespace FinanceSentry.Core.Interfaces;

/// <summary>One brokerage symbol's per-share USD price (value / quantity) as the last capture of one UTC day recorded it.</summary>
public sealed record DailySymbolPrice(string Symbol, DateOnly Date, decimal PricePerShare);

/// <summary>
/// Read port over the Risk module's per-position <c>holding_snapshots</c> history: what each brokerage symbol was
/// priced at on earlier days. Lets Wealth tell a quote that has stopped moving from one that is merely unchanged for
/// a day, without reaching into Risk's schema. Runs with no person in scope (a Hangfire job), so implementations opt
/// out of the Owner query filter and keep their own user predicate.
/// </summary>
public interface IBrokeragePriceHistoryReader
{
    /// <summary>
    /// One entry per symbol per UTC day from <paramref name="from"/> that has a capture of it, ordered by date; a day's
    /// price is its latest capture. Positions with no quantity are left out (no price per share). Days with no capture
    /// are absent.
    /// </summary>
    Task<IReadOnlyList<DailySymbolPrice>> GetDailyPricesAsync(Guid userId, DateOnly from, CancellationToken ct = default);
}
