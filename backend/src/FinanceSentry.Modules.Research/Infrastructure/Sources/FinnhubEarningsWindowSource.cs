namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

using System.Globalization;
using System.Net;
using System.Text.Json;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Screens;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Ports;
using Microsoft.Extensions.Options;

/// <summary>
/// Finnhub <c>/calendar/earnings?from=&amp;to=</c>: every company reporting inside a date window, in one request (free
/// tier). Rides the named client and key the recommendation-trends integration already configures
/// (<c>AnalystSources:Finnhub</c>, key in the <c>X-Finnhub-Token</c> header, the client's resilience handler honouring
/// <c>Retry-After</c>), so one run is one request against a 60-a-minute allowance. No key: <see cref="IsConfigured"/> is false.
/// </summary>
public sealed class FinnhubEarningsWindowSource(
    IHttpClientFactory httpFactory,
    IOptions<AnalystSourcesOptions> options) : IEarningsWindowSource
{
    public const string SourceName = "finnhub";

    public bool IsConfigured =>
        options.Value.Finnhub.Enabled && !string.IsNullOrWhiteSpace(options.Value.Finnhub.ApiKey);

    public async Task<IReadOnlyList<EarningsCalendarItem>> GetWindowAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return [];
        }

        var client = httpFactory.CreateClient(FinnhubRecommendationTrendsService.HttpClientName);
        var url = string.Create(CultureInfo.InvariantCulture, $"calendar/earnings?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        try
        {
            using var response = await client.GetAsync(url, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new EarningsCalendarProviderException(
                    $"Finnhub returned {(int)response.StatusCode} for the earnings calendar - invalid API key or endpoint moved behind a paywall.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new EarningsCalendarProviderException($"Finnhub returned {(int)response.StatusCode} for the earnings calendar.");
            }

            return Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or EarningsCalendarProviderException))
        {
            throw new EarningsCalendarProviderException($"Finnhub earnings calendar fetch failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses the <c>{"earningsCalendar":[{"symbol":..,"date":..}]}</c> body. Public and static so the contract test pins the
    /// documented shape; a row without a symbol or a valid date is skipped, a body that is not that object fails loudly.
    /// </summary>
    public static IReadOnlyList<EarningsCalendarItem> Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new EarningsCalendarProviderException($"Finnhub earnings calendar body is not JSON ({ex.Message}).");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind is not JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("earningsCalendar", out var rows)
                || rows.ValueKind is not JsonValueKind.Array)
            {
                throw new EarningsCalendarProviderException("Finnhub earnings calendar body is not the documented earningsCalendar object - contract drift.");
            }

            var items = new List<EarningsCalendarItem>();
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind is not JsonValueKind.Object
                    || !row.TryGetProperty("symbol", out var symbol) || symbol.ValueKind is not JsonValueKind.String
                    || string.IsNullOrWhiteSpace(symbol.GetString())
                    || !row.TryGetProperty("date", out var date) || date.ValueKind is not JsonValueKind.String
                    || !DateOnly.TryParse(date.GetString(), CultureInfo.InvariantCulture, out var eventDate))
                {
                    continue;
                }

                items.Add(new EarningsCalendarItem(
                    symbol.GetString()!.Trim().ToUpperInvariant(), EarningsEventType.Earnings, eventDate, false, SourceName));
            }

            return items;
        }
    }
}
