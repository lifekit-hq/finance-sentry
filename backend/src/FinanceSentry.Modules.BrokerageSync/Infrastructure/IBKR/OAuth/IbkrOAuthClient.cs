using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.OAuth;

/// <summary>
/// Talks to IBKR's Web API over OAuth 1.0a. Derives a per-user live session
/// token (LST) via the Diffie-Hellman handshake, caches it until IBKR expires
/// it, and HMAC-signs each portfolio request with it. Replaces the per-user
/// IBeam gateway: no container, no password, no 2FA.
/// </summary>
public sealed class IbkrOAuthClient(
    HttpClient http,
    IOptions<IbkrOAuthOptions> options,
    ILogger<IbkrOAuthClient> logger)
{
    private const int NonceBytes = 16;
    private const int DhChallengeBytes = 32;
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    // IBKR's edge silently 403s callers with no User-Agent or HTTP/2 (see the
    // CPG client quirks); pin both on every request.
    private static readonly ProductInfoHeaderValue UserAgent = new("finance-sentry", "1.0");

    private readonly ConcurrentDictionary<Guid, LiveSessionToken> _tokens = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _tokenLocks = new();

    public async Task<IReadOnlyList<string>> GetAccountsAsync(IbkrOAuthCredentials credentials, CancellationToken ct = default)
    {
        using var response = await SendSignedGetAsync(credentials, "/v1/api/portfolio/accounts", ct);
        var accounts = await response.Content
            .ReadFromJsonAsync<List<IBKRPortfolioAccountResponse>>(ct) ?? [];

        return accounts
            .Select(a => a.AccountId ?? a.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();
    }

    public async Task<IReadOnlyList<IBKRPositionResponse>> GetPositionsAsync(
        IbkrOAuthCredentials credentials, string accountId, CancellationToken ct = default)
    {
        using var response = await SendSignedGetAsync(
            credentials, $"/v1/api/portfolio/{accountId}/positions/0", ct);
        return await response.Content.ReadFromJsonAsync<List<IBKRPositionResponse>>(ct) ?? [];
    }

    /// <summary>
    /// Reads the account's cash ledger (settled cash per currency). Keyed by currency code,
    /// plus a <c>"BASE"</c> aggregate row the caller is expected to skip.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IBKRLedgerEntry>> GetLedgerAsync(
        IbkrOAuthCredentials credentials, string accountId, CancellationToken ct = default)
    {
        using var response = await SendSignedGetAsync(
            credentials, $"/v1/api/portfolio/{accountId}/ledger", ct);
        return await response.Content.ReadFromJsonAsync<Dictionary<string, IBKRLedgerEntry>>(ct)
            ?? new Dictionary<string, IBKRLedgerEntry>();
    }

    private async Task<HttpResponseMessage> SendSignedGetAsync(
        IbkrOAuthCredentials credentials, string path, CancellationToken ct)
    {
        var token = await EnsureLiveSessionTokenAsync(credentials, ct);
        var url = BuildUrl(path);
        var parameters = BaseOAuthParameters(credentials, "HMAC-SHA256");
        var baseString = IbkrOAuthSigner.BuildBaseString("GET", url, parameters);
        var signature = IbkrOAuthSigner.SignHmacSha256(baseString, token.Bytes);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuthHeader(request, parameters, signature);

        var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // The cached LST may have been invalidated server-side; drop it so the
            // next call re-derives a fresh one.
            _tokens.TryRemove(credentials.UserId, out _);
            throw new BrokerAuthException(
                $"IBKR rejected the signed request to {path} ({(int)response.StatusCode}).", "IBKR", response.StatusCode);
        }
        response.EnsureSuccessStatusCode();
        return response;
    }

    private async Task<LiveSessionToken> EnsureLiveSessionTokenAsync(
        IbkrOAuthCredentials credentials, CancellationToken ct)
    {
        if (TryGetFreshToken(credentials.UserId, out var cached))
            return cached;

        var gate = _tokenLocks.GetOrAdd(credentials.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (TryGetFreshToken(credentials.UserId, out cached))
                return cached;

            var token = await RequestLiveSessionTokenAsync(credentials, ct);
            _tokens[credentials.UserId] = token;
            return token;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryGetFreshToken(Guid userId, out LiveSessionToken token)
    {
        if (_tokens.TryGetValue(userId, out var cached) &&
            cached.ExpiresAt - RefreshMargin > DateTimeOffset.UtcNow)
        {
            token = cached;
            return true;
        }
        token = default!;
        return false;
    }

    private async Task<LiveSessionToken> RequestLiveSessionTokenAsync(
        IbkrOAuthCredentials credentials, CancellationToken ct)
    {
        var secretBytes = IbkrOAuthSigner.DecryptAccessTokenSecret(
            credentials.AccessTokenSecret, credentials.EncryptionKey);
        var prependHex = IbkrOAuthSigner.ToHex(secretBytes);
        var (dhPrivate, challengeHex) = IbkrOAuthSigner.GenerateDhChallenge(
            credentials.DhPrime, credentials.DhGenerator);

        var url = BuildUrl("/v1/api/oauth/live_session_token");
        var parameters = BaseOAuthParameters(credentials, "RSA-SHA256");
        parameters["diffie_hellman_challenge"] = challengeHex;

        // The LST request signs (decrypted-secret-hex + base string) with RSA.
        var baseString = prependHex + IbkrOAuthSigner.BuildBaseString("POST", url, parameters);
        var signature = IbkrOAuthSigner.SignRsaSha256(baseString, credentials.SignatureKey);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyAuthHeader(request, parameters, signature);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new BrokerAuthException(
                $"IBKR live-session-token request failed ({(int)response.StatusCode}): {body}", "IBKR", response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<LiveSessionTokenResponse>(ct)
            ?? throw new BrokerAuthException("IBKR returned an empty live-session-token response.", "IBKR");

        var lst = IbkrOAuthSigner.ComputeLiveSessionToken(
            credentials.DhPrime, dhPrivate, payload.DiffieHellmanResponse, secretBytes);

        if (!IbkrOAuthSigner.ValidateLiveSessionToken(lst, credentials.ConsumerKey, payload.LiveSessionTokenSignature))
            throw new BrokerAuthException("IBKR live session token failed signature validation.", "IBKR");

        logger.LogInformation("Derived IBKR live session token for user {UserId}", credentials.UserId);
        return new LiveSessionToken(
            Convert.FromBase64String(lst),
            DateTimeOffset.FromUnixTimeMilliseconds(payload.LiveSessionTokenExpiration));
    }

    private Dictionary<string, string> BaseOAuthParameters(IbkrOAuthCredentials credentials, string signatureMethod) => new()
    {
        ["oauth_consumer_key"] = credentials.ConsumerKey,
        ["oauth_nonce"] = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(NonceBytes)),
        ["oauth_signature_method"] = signatureMethod,
        ["oauth_timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
        ["oauth_token"] = credentials.AccessToken,
    };

    private void ApplyAuthHeader(
        HttpRequestMessage request, IReadOnlyDictionary<string, string> parameters, string signature)
    {
        var header = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
            header[key] = value;
        header["oauth_signature"] = signature;

        var parts = new List<string> { $"realm=\"{options.Value.Realm}\"" };
        parts.AddRange(header.Select(p => $"{p.Key}=\"{IbkrOAuthSigner.PercentEncode(p.Value)}\""));

        request.Version = HttpVersion.Version11;
        request.Headers.UserAgent.Add(UserAgent);
        request.Headers.TryAddWithoutValidation("Authorization", "OAuth " + string.Join(", ", parts));
    }

    private string BuildUrl(string path) => options.Value.BaseUrl.TrimEnd('/') + path;

    private sealed record LiveSessionToken(byte[] Bytes, DateTimeOffset ExpiresAt);

    private sealed record LiveSessionTokenResponse(
        [property: JsonPropertyName("diffie_hellman_response")] string DiffieHellmanResponse,
        [property: JsonPropertyName("live_session_token_signature")] string LiveSessionTokenSignature,
        [property: JsonPropertyName("live_session_token_expiration")] long LiveSessionTokenExpiration);
}
