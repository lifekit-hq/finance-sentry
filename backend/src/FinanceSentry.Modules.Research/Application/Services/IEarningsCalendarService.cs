namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;

public interface IEarningsCalendarService
{
    // Fetches upcoming earnings + ex-dividend/dividend dates for the given tickers, live from
    // Yahoo Finance, filtered to [from, to] and (optionally) a single eventType. A per-ticker
    // fetch failure never surfaces by default: the ticker simply contributes no events, same as
    // "nothing scheduled". Pass surfaceProviderFailure: true to opt into
    // EarningsCalendarProviderException when EVERY requested ticker's fetch failed (a real
    // outage) — a partial failure still returns whichever tickers succeeded.
    Task<IReadOnlyList<EarningsEvent>> GetForTickersAsync(
        IReadOnlyCollection<string> tickers,
        DateOnly from,
        DateOnly to,
        string? eventType,
        CancellationToken ct = default,
        bool surfaceProviderFailure = false);
}
