namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Implements Radar's <see cref="IIncomeOutlookSource"/> over Research's published <see cref="IDividendYieldReader"/>
/// and <see cref="IEarningsCalendarReader"/> ports (ex-dividend rows). Lives in Integration so Modules.Radar never
/// references Modules.Research. A calendar failure leaves the ex-dividend dates out; it never fails the yield.
/// </summary>
public sealed class RadarIncomeOutlookAdapter(IDividendYieldReader yields, IEarningsCalendarReader calendar) : IIncomeOutlookSource
{
    private const string ExDividend = "ex_dividend";

    public async Task<IReadOnlyDictionary<string, IncomeOutlook>> GetAsync(
        IReadOnlyCollection<string> tickers, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (tickers.Count == 0)
            return new Dictionary<string, IncomeOutlook>();

        var latestYields = await yields.GetLatestAsync(tickers, ct);

        var exDates = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        try
        {
            foreach (var e in await calendar.GetForTickersAsync([.. tickers], from, to, ct))
            {
                if (!string.Equals(e.EventType, ExDividend, StringComparison.OrdinalIgnoreCase))
                    continue;
                var key = e.Ticker.ToUpperInvariant();
                if (!exDates.TryGetValue(key, out var known) || e.EventDate < known)
                    exDates[key] = e.EventDate;
            }
        }
        catch (EarningsCalendarProviderException)
        {
        }

        return tickers
            .Select(t => t.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Where(t => latestYields.ContainsKey(t) || exDates.ContainsKey(t))
            .ToDictionary(
                t => t,
                t => new IncomeOutlook(
                    latestYields.TryGetValue(t, out var y) ? y : null,
                    exDates.TryGetValue(t, out var d) ? d : null),
                StringComparer.Ordinal);
    }
}
