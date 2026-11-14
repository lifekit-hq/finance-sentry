namespace FinanceSentry.Modules.Research.Application.Services.Screens;

using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// The market-wide earnings calendar for a date window in one request (Finnhub's <c>/calendar/earnings</c>): the
/// dated-catalyst screen intersects it with the reference universe instead of asking per ticker.
/// </summary>
public interface IEarningsWindowSource
{
    /// <summary>False when no key is configured; the screen then skips quietly.</summary>
    bool IsConfigured { get; }

    /// <summary>Every earnings event dated within [<paramref name="from"/>, <paramref name="to"/>]. Throws <see cref="EarningsCalendarProviderException"/> when the provider cannot answer, so an outage is not read as "nothing is reporting".</summary>
    Task<IReadOnlyList<EarningsCalendarItem>> GetWindowAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
