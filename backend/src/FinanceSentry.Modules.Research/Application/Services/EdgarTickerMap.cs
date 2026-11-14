namespace FinanceSentry.Modules.Research.Application.Services;

using System.Globalization;
using System.Text.Json;
using FinanceSentry.Core.Interfaces;
using Microsoft.Extensions.Logging;

/// <summary>
/// How <see cref="EdgarTickerMap"/> keeps the SEC ticker→CIK table fresh and what it does when the
/// refresh fails (#1375). The defaults are production values; tests pass zero delays.
/// </summary>
public sealed record EdgarTickerMapOptions
{
    /// <summary>A loaded map is refreshed once it is older than this.</summary>
    public TimeSpan Ttl { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// The oldest a map may be while refreshes keep failing and it is still served. Past it the map is
    /// treated as unavailable: CIKs are stable, but a week-old table misses every new listing, and a
    /// loud failure beats a silently incomplete answer.
    /// </summary>
    public TimeSpan MaxStale { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Fetch attempts per refresh before it is counted as failed.</summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>The wait before attempt 2, then attempt 3 (the last value repeats if more attempts are configured).</summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } = [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2)];

    /// <summary>
    /// After a refresh has failed, callers are answered from what is held for this long instead of each
    /// one running its own retry round against a provider that is already refusing.
    /// </summary>
    public TimeSpan FailureCooldown { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>The map as one caller sees it. <see cref="Map"/> is null when nothing usable could be loaded.</summary>
public sealed record EdgarTickerMapRead(IReadOnlyDictionary<string, string>? Map, bool Stale);

/// <summary>
/// The SEC ticker→CIK table, loaded from <c>company_tickers.json</c> and held in memory (#1375).
/// A refresh retries with backoff; while it keeps failing the last good copy is served until
/// <see cref="EdgarTickerMapOptions.MaxStale"/>, and every failure is logged at error level and
/// reported through <see cref="IDataSourceHealthReader"/> so a map outage is observable instead of
/// reading as "no filings". Registered inside the singleton <see cref="SecEdgarService"/>.
/// </summary>
public sealed class EdgarTickerMap(
    IHttpClientFactory httpFactory,
    TimeProvider clock,
    ILogger logger,
    EdgarTickerMapOptions options) : IDataSourceHealthReader, IDisposable
{
    public const string SourceName = "sec-edgar";

    private const string TickerMapUrl = "https://www.sec.gov/files/company_tickers.json";
    private const int CikWidth = 10;

    private readonly SemaphoreSlim gate = new(1, 1);

    private IReadOnlyDictionary<string, string>? map;
    private DateTimeOffset? loadedAt;
    private DateTimeOffset? lastFailureAt;
    private string? lastFailure;
    private int consecutiveFailures;

    public async Task<EdgarTickerMapRead> GetAsync(string httpClientName, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (IsFresh(now))
        {
            return new EdgarTickerMapRead(map, Stale: false);
        }

        await gate.WaitAsync(ct);
        try
        {
            now = clock.GetUtcNow();
            if (IsFresh(now))
            {
                return new EdgarTickerMapRead(map, Stale: false);
            }

            // A refresh just failed: answer from what is held rather than run another retry round.
            if (lastFailureAt is { } failedAt && now - failedAt < options.FailureCooldown)
            {
                return Held(now);
            }

            if (await TryRefreshAsync(httpClientName, ct))
            {
                return new EdgarTickerMapRead(map, Stale: false);
            }

            return Held(now);
        }
        finally
        {
            gate.Release();
        }
    }

    public DataSourceHealth Read()
    {
        var now = clock.GetUtcNow();
        var state = (map is not null, lastFailureAt is not null) switch
        {
            (true, false) => DataSourceHealthState.Healthy,
            (true, true) when IsFresh(now) => DataSourceHealthState.Healthy,
            (true, true) when now - loadedAt!.Value < options.MaxStale => DataSourceHealthState.Stale,
            (false, false) => DataSourceHealthState.NeverChecked,
            _ => DataSourceHealthState.Unavailable,
        };

        var detail = state switch
        {
            DataSourceHealthState.Stale =>
                $"ticker→CIK map refresh is failing ({consecutiveFailures} in a row); serving the copy from {loadedAt:u}. {lastFailure}",
            DataSourceHealthState.Unavailable =>
                $"ticker→CIK map is unavailable ({consecutiveFailures} refreshes failed): fundamentals and filings cannot resolve any ticker. {lastFailure}",
            _ => null,
        };

        return new DataSourceHealth(SourceName, state, loadedAt, lastFailureAt, consecutiveFailures, detail);
    }

    public void Dispose() => gate.Dispose();

    private bool IsFresh(DateTimeOffset now) => map is not null && loadedAt is { } at && now - at < options.Ttl;

    // What the map can still offer after a failed or skipped refresh: the last good copy within the
    // stale ceiling, otherwise nothing (the caller reports map_unavailable).
    private EdgarTickerMapRead Held(DateTimeOffset now)
        => map is not null && loadedAt is { } at && now - at < options.MaxStale
            ? new EdgarTickerMapRead(map, Stale: true)
            : new EdgarTickerMapRead(null, Stale: false);

    private async Task<bool> TryRefreshAsync(string httpClientName, CancellationToken ct)
    {
        var attempts = Math.Max(1, options.MaxAttempts);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                map = await FetchAsync(httpClientName, ct);
                loadedAt = clock.GetUtcNow();
                if (consecutiveFailures > 0)
                {
                    logger.LogInformation(
                        "EDGAR ticker->CIK map recovered after {Failures} failed refreshes ({Count} tickers)",
                        consecutiveFailures, map.Count);
                }

                consecutiveFailures = 0;
                lastFailureAt = null;
                lastFailure = null;
                return true;
            }
            // An HttpClient timeout is a TaskCanceledException too: only the caller's own cancellation propagates.
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                lastFailure = $"{ex.GetType().Name}: {ex.Message}";
                if (attempt == attempts)
                {
                    consecutiveFailures++;
                    lastFailureAt = clock.GetUtcNow();
                    logger.LogError(
                        ex,
                        "EDGAR ticker->CIK map refresh failed after {Attempts} attempts ({Failures} refreshes in a row); {Outcome}",
                        attempts,
                        consecutiveFailures,
                        map is null
                            ? "no map is loaded, so fundamentals and filings cannot resolve any ticker"
                            : $"serving the copy loaded at {loadedAt:u}");
                    return false;
                }

                logger.LogWarning(ex, "EDGAR ticker->CIK map fetch attempt {Attempt}/{Attempts} failed; retrying", attempt, attempts);
                await Task.Delay(RetryDelay(attempt), clock, ct);
            }
        }

        return false;
    }

    private TimeSpan RetryDelay(int failedAttempt)
        => options.RetryDelays.Count == 0
            ? TimeSpan.Zero
            : options.RetryDelays[Math.Min(failedAttempt, options.RetryDelays.Count) - 1];

    private async Task<IReadOnlyDictionary<string, string>> FetchAsync(string httpClientName, CancellationToken ct)
    {
        var client = httpFactory.CreateClient(httpClientName);
        using var response = await client.GetAsync(TickerMapUrl, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in doc.RootElement.EnumerateObject())
        {
            var row = entry.Value;
            if (!row.TryGetProperty("ticker", out var t) || !row.TryGetProperty("cik_str", out var c))
            {
                continue;
            }

            var symbol = t.GetString();
            if (!string.IsNullOrEmpty(symbol) && c.ValueKind == JsonValueKind.Number)
            {
                // EDGAR paths use the 10-digit zero-padded CIK.
                parsed[symbol] = c.GetInt64().ToString(CultureInfo.InvariantCulture).PadLeft(CikWidth, '0');
            }
        }

        // A 200 whose body parses to no rows is a broken payload, not "no tickers": treat it as a failure
        // so it is retried and reported instead of resolving every ticker to "not a filer".
        return parsed.Count == 0
            ? throw new InvalidOperationException("EDGAR ticker map payload contained no usable rows")
            : parsed;
    }
}
