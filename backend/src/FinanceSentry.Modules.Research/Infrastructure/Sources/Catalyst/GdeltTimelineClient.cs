namespace FinanceSentry.Modules.Research.Infrastructure.Sources.Catalyst;

using System.Globalization;
using System.Text.Json;
using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// GDELT DOC 2.0 <c>timelinevolraw</c>: raw article counts per day for a quoted phrase over the last N days (English-language
/// sources only). GDELT answers plain text, not JSON, when it throttles (HTTP 429, "limit requests to one every 5 seconds") or
/// rejects a query, and sometimes an empty body: every one of those raises <see cref="CatalystFeedException"/> instead of reading
/// as "no articles", so a throttled run records a failure, never a zero.
/// </summary>
public sealed class GdeltTimelineClient(
    IHttpClientFactory httpFactory,
    [FromKeyedServices(CatalystPacers.Gdelt)] RequestPacer pacer,
    IOptions<CatalystOptions> options) : IGdeltTimelineClient
{
    public const string HttpClientName = "gdelt";

    private readonly CatalystOptions _options = options.Value;

    public async Task<IReadOnlyList<GdeltDay>> GetDailyVolumeAsync(string phrase, int days, CancellationToken ct = default)
    {
        var query = Uri.EscapeDataString($"\"{phrase.Replace("\"", string.Empty, StringComparison.Ordinal)}\" sourcelang:english");
        var url = string.Create(CultureInfo.InvariantCulture,
            $"{_options.GdeltUrl}?query={query}&mode=timelinevolraw&timespan={Math.Max(1, days)}d&format=json");

        string body;
        try
        {
            await pacer.WaitAsync(ct);
            using var response = await httpFactory.CreateClient(HttpClientName).GetAsync(url, ct);
            body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new CatalystFeedException($"GDELT answered HTTP {(int)response.StatusCode} for \"{phrase}\".");
            }
        }
        catch (CatalystFeedException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new CatalystFeedException($"GDELT could not be read for \"{phrase}\": {ex.Message}", ex);
        }

        return Parse(body, phrase);
    }

    /// <summary>The daily counts of a <c>timelinevolraw</c> body. Public so the shape is pinned by tests.</summary>
    public static IReadOnlyList<GdeltDay> Parse(string body, string phrase)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new CatalystFeedException($"GDELT answered an empty body for \"{phrase}\".");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new CatalystFeedException($"GDELT answered something that is not JSON for \"{phrase}\".", ex);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new CatalystFeedException($"GDELT answered an unexpected JSON shape for \"{phrase}\".");
            }

            // No articles at all is an object without a timeline; that is a measurement of zero coverage, not a failure.
            if (!doc.RootElement.TryGetProperty("timeline", out var timeline) || timeline.ValueKind != JsonValueKind.Array
                || timeline.GetArrayLength() == 0)
            {
                return [];
            }

            var series = timeline[0];
            if (!series.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                throw new CatalystFeedException($"GDELT answered a timeline without data for \"{phrase}\".");
            }

            var days = new List<GdeltDay>();
            foreach (var point in data.EnumerateArray())
            {
                if (!point.TryGetProperty("date", out var date) || date.ValueKind != JsonValueKind.String
                    || !DateTime.TryParseExact(date.GetString(), "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
                    || !point.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                long? total = point.TryGetProperty("norm", out var norm) && norm.ValueKind == JsonValueKind.Number
                    ? (long)Math.Round(norm.GetDouble())
                    : null;
                days.Add(new GdeltDay(DateOnly.FromDateTime(at), (long)Math.Round(value.GetDouble()), total));
            }

            return
            [
                .. days.GroupBy(d => d.Day)
                    .Select(g => new GdeltDay(
                        g.Key,
                        g.Sum(d => d.Articles),
                        g.Any(d => d.TotalArticles is not null) ? g.Sum(d => d.TotalArticles ?? 0) : null))
                    .OrderBy(d => d.Day),
            ];
        }
    }
}
