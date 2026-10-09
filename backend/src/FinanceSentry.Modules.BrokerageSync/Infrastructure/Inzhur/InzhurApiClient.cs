using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

public interface IInzhurApiClient
{
    /// <summary>Refreshes the cabinet session: a new access token, and the jar as the auth host left it.</summary>
    Task<InzhurSession> RefreshAsync(InzhurSession session, CancellationToken ct = default);

    /// <summary>Reads holdings and the broker cash account with the session's access token.</summary>
    Task<InzhurPortfolio> GetPortfolioAsync(InzhurSession session, CancellationToken ct = default);
}

/// <summary>
/// The daily, cookie-and-bearer side of the cabinet: one refresh and two GETs. Read-only by construction — the only
/// POST is the session refresh; there is no trade, withdrawal or logout call here (logout would revoke the refresh
/// token). Cookies are carried per call from the stored jar, never in a handler-wide container, so one user's session
/// can never ride along on another user's request.
/// </summary>
public sealed class InzhurApiClient(
    HttpClient http,
    IOptions<InzhurOptions> options,
    ILogger<InzhurApiClient> logger) : IInzhurApiClient
{
    /// <summary>Per-request ceiling; a slow Inzhur is retried by the job hours later, not held open.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private const int MaxErrorTextLength = 200;

    // Inzhur's own error texts that mean the session is gone (design report §1, "log in again").
    private static readonly string[] ReloginMarkers =
    [
        "Missing refresh token",
        "Invalid or expired refresh token",
        "Missing or invalid credentials",
        "Unauthorized",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InzhurSession> RefreshAsync(InzhurSession session, CancellationToken ct = default)
    {
        var uri = new Uri($"{options.Value.AuthBaseUrl.TrimEnd('/')}/api/v1/auth/refresh");
        var jar = session.ToContainer();

        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent([]) };
        AddCabinetHeaders(request);
        request.Headers.TryAddWithoutValidation("Origin", options.Value.CabinetOrigin);
        AddCookies(request, jar, uri);

        using var response = await SendAsync(request, "refresh", ct);
        var issued = new CookieContainer();
        StoreCookies(response, issued, uri);

        if (!response.IsSuccessStatusCode)
            throw await FailureAsync(response, "refresh", ct);

        var body = await ReadAsync<InzhurRefreshResponse>(response, "refresh", ct);
        if (string.IsNullOrEmpty(body?.AccessToken))
            throw new InzhurApiException(InzhurFailureKind.Unexpected, "Inzhur refresh returned no access token.");

        return InzhurSession.FromContainer(body.AccessToken, Merge(jar, issued));
    }

    // A cookie the auth host sets again supersedes every stored cookie of that name, whatever its domain or path: the
    // pasted refresh cookie is stored host-wide on "/", and a rotated one set under another path would otherwise sit
    // beside it and send both on the next refresh.
    private static CookieContainer Merge(CookieContainer jar, CookieContainer issued)
    {
        var reissued = issued.GetAllCookies().Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var merged = new CookieContainer();
        foreach (var cookie in jar.GetAllCookies().Where(c => !reissued.Contains(c.Name)))
            merged.Add(cookie);
        merged.Add(issued.GetAllCookies());
        return merged;
    }

    public async Task<InzhurPortfolio> GetPortfolioAsync(InzhurSession session, CancellationToken ct = default)
    {
        var assets = await GetAsync<InzhurUserAssetsResponse>("api/v1/user-assets", session, ct);
        var account = await GetAsync<InzhurBrokerAccount>("api/v1/users/broker-account", session, ct);

        // A body without the portfolio containers is a changed cabinet, not an empty portfolio: failing here keeps
        // the stored holdings, where reading it as empty would reconcile every Inzhur row away.
        if (assets?.Assets is null)
            throw new InzhurApiException(InzhurFailureKind.Unexpected, "Inzhur user-assets returned no assets list.");
        if (account is null || (account.AvailableBalanceUah is null && account.BlockedBalanceUah is null))
            throw new InzhurApiException(InzhurFailureKind.Unexpected, "Inzhur broker-account returned no balances.");

        return new InzhurPortfolio(assets.Assets, account);
    }

    private async Task<T?> GetAsync<T>(string path, InzhurSession session, CancellationToken ct)
    {
        var uri = new Uri($"{options.Value.CoreBaseUrl.TrimEnd('/')}/{path}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        AddCabinetHeaders(request);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        AddCookies(request, session.ToContainer(), uri);

        using var response = await SendAsync(request, path, ct);
        if (!response.IsSuccessStatusCode)
            throw await FailureAsync(response, path, ct);

        return await ReadAsync<T>(response, path, ct);
    }

    private static void AddCabinetHeaders(HttpRequestMessage request)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("x-client-platform", "web");
        request.Headers.TryAddWithoutValidation("X-Lang", "uk");
    }

    private static void AddCookies(HttpRequestMessage request, CookieContainer jar, Uri uri)
    {
        var header = jar.GetCookieHeader(uri);
        if (header.Length > 0)
            request.Headers.TryAddWithoutValidation("Cookie", header);
    }

    private void StoreCookies(HttpResponseMessage response, CookieContainer jar, Uri uri)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return;

        foreach (var value in values)
        {
            try
            {
                jar.SetCookies(uri, value);
            }
            catch (CookieException)
            {
                // The value itself is a secret; only the fact is logged.
                logger.LogWarning("Inzhur refresh returned a Set-Cookie header that could not be parsed; it was skipped.");
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string operation, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new InzhurApiException(
                InzhurFailureKind.Unavailable, $"Inzhur {operation} failed: {ex.GetType().Name}.", ex);
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            throw new InzhurApiException(
                InzhurFailureKind.Unexpected, $"Inzhur {operation} returned a body of an unexpected shape.", ex);
        }
    }

    private async Task<InzhurApiException> FailureAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        var errorText = await ReadErrorTextAsync(response, ct);
        var kind = Classify(response.StatusCode, errorText);

        logger.LogWarning(
            "Inzhur {Operation} returned {StatusCode} ({ErrorText}); classified {FailureKind}",
            operation, status, errorText ?? "no error text", kind);

        return new InzhurApiException(
            kind, $"Inzhur {operation} returned {status}{(errorText is null ? string.Empty : $": {errorText}")}.");
    }

    internal static InzhurFailureKind Classify(HttpStatusCode status, string? errorText)
    {
        if (status == HttpStatusCode.TooManyRequests)
            return InzhurFailureKind.RateLimited;
        if ((int)status >= 500)
            return InzhurFailureKind.Unavailable;
        if (status == HttpStatusCode.Unauthorized)
            return InzhurFailureKind.ReauthRequired;
        if (errorText is not null && ReloginMarkers.Any(m => errorText.Contains(m, StringComparison.OrdinalIgnoreCase)))
            return InzhurFailureKind.ReauthRequired;
        return InzhurFailureKind.Unexpected;
    }

    // Inzhur errors read { error: { message, details } } (the cabinet's own handler reads error.message). Only that
    // message is kept: it is Inzhur's fixed text, never an echo of a token or the request.
    private static async Task<string?> ReadErrorTextAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            var message = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString()
                    : root.ValueKind == JsonValueKind.Object
                      && root.TryGetProperty("message", out var top) && top.ValueKind == JsonValueKind.String
                        ? top.GetString()
                        : null;

            return message is null || message.Length <= MaxErrorTextLength ? message : message[..MaxErrorTextLength];
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
