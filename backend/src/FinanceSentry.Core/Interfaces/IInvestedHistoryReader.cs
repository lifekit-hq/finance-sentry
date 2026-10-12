namespace FinanceSentry.Core.Interfaces;

/// <summary>Invested (non-cash) value held on one UTC day, per sleeve, as the last capture of that day recorded it.</summary>
public sealed record DailyInvestedBySleeve(DateOnly Date, decimal BrokerageUsd, decimal CryptoUsd);

/// <summary>
/// Read port over the Risk module's per-position <c>holding_snapshots</c> history, which stores invested
/// positions only (cash is not a position). Lets Wealth reconstruct the invested/cash split of past
/// net-worth snapshots without reaching into Risk's schema. Runs with no person in scope (a startup job),
/// so implementations opt out of the Owner query filter and keep their own user predicate.
/// </summary>
public interface IInvestedHistoryReader
{
    /// <summary>
    /// One entry per UTC day from <paramref name="from"/> that has at least one capture, ordered by date. A day's
    /// value is its latest capture. Days with no capture are absent.
    /// </summary>
    Task<IReadOnlyList<DailyInvestedBySleeve>> GetDailyAsync(Guid userId, DateOnly from, CancellationToken ct = default);
}
