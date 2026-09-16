using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using Microsoft.Extensions.Configuration;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;

/// <summary>
/// Signed calls to the Revolut X REST API (<c>https://revx.revolut.com/api/1.0</c>). Read-only
/// endpoints only: a read-only key is all Finance Sentry asks the user for.
/// </summary>
public sealed class RevolutXHttpClient(HttpClient httpClient, IConfiguration configuration, TimeProvider timeProvider)
{
    public const string ApiPrefix = "/api/1.0";
    private const string DefaultBaseUrl = "https://revx.revolut.com";

    private readonly string _baseUrl = (configuration["RevolutX:BaseUrl"] ?? DefaultBaseUrl).TrimEnd('/');

    public async Task<IReadOnlyList<RevolutXBalance>> GetBalancesAsync(
        RevolutXCredentials credentials, CancellationToken ct = default) =>
        await SendSignedAsync<IReadOnlyList<RevolutXBalance>>(credentials, "/balances", ct);

    public async Task<IReadOnlyDictionary<string, RevolutXCurrency>> GetCurrenciesAsync(
        RevolutXCredentials credentials, CancellationToken ct = default) =>
        await SendSignedAsync<Dictionary<string, RevolutXCurrency>>(credentials, "/configuration/currencies", ct);

    public Task<RevolutXTickersResponse> GetTickersAsync(
        RevolutXCredentials credentials, CancellationToken ct = default) =>
        SendSignedAsync<RevolutXTickersResponse>(credentials, "/tickers", ct);

    /// <summary><c>GET /configuration/pairs</c> — keyed by the slash form (<c>BTC/USD</c>).</summary>
    public async Task<IReadOnlyDictionary<string, RevolutXPair>> GetPairsAsync(
        RevolutXCredentials credentials, CancellationToken ct = default) =>
        await SendSignedAsync<Dictionary<string, RevolutXPair>>(credentials, "/configuration/pairs", ct);

    /// <summary>
    /// <c>GET /trades/private/{symbol}</c> — the user's own fills on one pair between two instants
    /// (Unix ms). <paramref name="symbol"/> is the path form (<c>BTC-USD</c>); the venue serves at
    /// most a one-week window per call and pages within it by <paramref name="cursor"/>.
    /// </summary>
    public Task<RevolutXTradesResponse> GetPrivateTradesAsync(
        RevolutXCredentials credentials,
        string symbol,
        long startMs,
        long endMs,
        string? cursor,
        int limit,
        CancellationToken ct = default)
    {
        var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["end_date"] = endMs.ToString(CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
            ["start_date"] = startMs.ToString(CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrEmpty(cursor))
        {
            query["cursor"] = cursor;
        }

        return SendSignedAsync<RevolutXTradesResponse>(
            credentials, $"/trades/private/{Uri.EscapeDataString(symbol)}", ct, BuildQuery(query));
    }

    /// <summary>Keys in ordinal order, each part percent-encoded — the exact string that is signed.</summary>
    private static string BuildQuery(SortedDictionary<string, string> parameters) =>
        string.Join('&', parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    private async Task<T> SendSignedAsync<T>(
        RevolutXCredentials credentials,
        string path,
        CancellationToken ct,
        string query = "")
    {
        var fullPath = ApiPrefix + path;
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var signature = RevolutXSigner.Sign(
            credentials.PrivateKey,
            RevolutXSigner.BuildMessage(timestamp, HttpMethod.Get.Method, fullPath, query, body: string.Empty));

        var url = _baseUrl + fullPath + (query.Length > 0 ? "?" + query : string.Empty);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add(RevolutXSigner.ApiKeyHeader, credentials.ApiKey);
        request.Headers.Add(RevolutXSigner.TimestampHeader, timestamp);
        request.Headers.Add(RevolutXSigner.SignatureHeader, signature);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new RevolutXException("Revolut X is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new RevolutXException("Revolut X did not respond in time.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var detail = TryReadErrorMessage(body) ?? response.ReasonPhrase ?? "request failed";
                var status = (int)response.StatusCode;
                throw new RevolutXException(
                    $"Revolut X API error (HTTP {status}): {detail}",
                    new HttpRequestException(detail, inner: null, response.StatusCode));
            }

            try
            {
                return JsonSerializer.Deserialize<T>(body)
                    ?? throw new RevolutXException($"Revolut X returned an empty response for {path}.");
            }
            catch (JsonException ex)
            {
                throw new RevolutXException($"Revolut X returned an unreadable response for {path}.", ex);
            }
        }
    }

    private static string? TryReadErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RevolutXErrorResponse>(body)?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
