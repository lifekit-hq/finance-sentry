using FinanceSentry.Modules.Radar.Domain;

namespace FinanceSentry.Modules.Radar.Application.Services;

public interface IBookPerformanceService
{
    /// <summary>
    /// Computes the change in invested value (includes deposits; not a time-weighted return) for the user's invested
    /// brokerage portfolio (broker cash excluded) vs SPY's adjusted-close change for the given lookback periods.
    /// Periods with insufficient history are omitted from the result set.
    /// </summary>
    Task<BookPerformanceResult> GetAsync(
        Guid userId,
        IReadOnlyList<BookPerformancePeriod> periods,
        CancellationToken ct = default);
}
