using FinanceSentry.Modules.Radar.Domain;

namespace FinanceSentry.Modules.Radar.Application.Services;

public interface IBookPerformanceService
{
    /// <summary>
    /// Computes TWR for the user's invested brokerage portfolio (broker cash excluded) vs SPY for the given lookback periods.
    /// Periods with insufficient history are omitted from the result set.
    /// </summary>
    Task<BookPerformanceResult> GetAsync(
        Guid userId,
        IReadOnlyList<BookPerformancePeriod> periods,
        CancellationToken ct = default);
}
