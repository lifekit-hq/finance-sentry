using FinanceSentry.Modules.Radar.Domain;

namespace FinanceSentry.Modules.Radar.Application.Services;

public interface IBookPerformanceService
{
    /// <summary>
    /// Computes the time-weighted return of the user's brokerage portfolio (broker cash included, the brokers'
    /// deposits and withdrawals taken out) vs SPY's adjusted-close return over the same dates for the given lookback
    /// periods.
    /// Periods with insufficient history are omitted from the result set.
    /// </summary>
    Task<BookPerformanceResult> GetAsync(
        Guid userId,
        IReadOnlyList<BookPerformancePeriod> periods,
        CancellationToken ct = default);
}
