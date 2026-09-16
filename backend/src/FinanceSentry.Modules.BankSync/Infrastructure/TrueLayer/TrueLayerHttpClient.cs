namespace FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public sealed class TrueLayerHttpClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<TrueLayerHttpClient> logger) : ITrueLayerClient
{
    public const string AuthClientName = "truelayer-auth";
    public const string ApiClientName = "truelayer-api";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _clientId = configuration["TrueLayer:ClientId"] ?? string.Empty;
    private readonly string _clientSecret = configuration["TrueLayer:ClientSecret"] ?? string.Empty;
    private readonly string _authBaseUrl = (configuration["TrueLayer:AuthBaseUrl"] ?? "https://auth.truelayer-sandbox.com").TrimEnd('/');
    private readonly string _scopes = configuration["TrueLayer:Scopes"] ?? "info accounts balance transactions cards offline_access";

    public async Task<IReadOnlyList<TrueLayerProvider>> ListProvidersAsync(string? country, CancellationToken ct = default)
    {
        EnsureConfigured();
        var auth = httpClientFactory.CreateClient(AuthClientName);
        var entries = await auth.GetFromJsonAsync<List<ProviderEntry>>(
            $"/api/providers?clientId={Uri.EscapeDataString(_clientId)}", JsonOpts, ct)
            ?? [];

        return entries
            .Where(p => string.IsNullOrEmpty(country)
                || (p.Country ?? string.Empty).Equals(country, StringComparison.OrdinalIgnoreCase))
            .Select(p => new TrueLayerProvider(
                ProviderId: p.ProviderId,
                DisplayName: p.DisplayName,
                Country: p.Country ?? string.Empty,
                LogoUrl: p.LogoUrl))
            .ToList();
    }

    public string BuildAuthLink(string providerId, string reference, string redirectUri)
    {
        EnsureConfigured();
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["response_type"] = "code";
        qs["client_id"] = _clientId;
        qs["scope"] = _scopes;
        qs["redirect_uri"] = redirectUri;
        qs["providers"] = providerId;
        qs["provider_id"] = providerId;
        qs["state"] = reference;
        return $"{_authBaseUrl}/?{qs}";
    }

    public async Task<TrueLayerTokenSet> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        EnsureConfigured();
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["redirect_uri"] = redirectUri,
            ["code"] = code,
        };
        return await PostTokenAsync(body, ct);
    }

    public async Task<TrueLayerTokenSet> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        EnsureConfigured();
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["refresh_token"] = refreshToken,
        };
        return await PostTokenAsync(body, ct);
    }

    public async Task<IReadOnlyList<TrueLayerAccountInfo>> ListAccountsAsync(string accessToken, CancellationToken ct = default)
    {
        var raw = await GetWithBearerAsync<AccountsResponse>(accessToken, "/data/v1/accounts", ct);
        return (raw.Results ?? []).Select(MapAccount).ToList();
    }

    public async Task<TrueLayerAccountBalance?> GetBalanceAsync(string accessToken, string accountId, CancellationToken ct = default)
    {
        var raw = await GetWithBearerAsync<BalanceResponse>(
            accessToken, $"/data/v1/accounts/{Uri.EscapeDataString(accountId)}/balance", ct);

        var entry = raw.Results?.FirstOrDefault();
        if (entry is null)
            return null;

        return new TrueLayerAccountBalance(
            Current: entry.Current,
            Available: entry.Available ?? entry.Current,
            Currency: entry.Currency ?? "EUR");
    }

    public async Task<IReadOnlyList<TrueLayerTransaction>> GetTransactionsAsync(
        string accessToken, string accountId, DateOnly? dateFrom, DateOnly? dateTo, CancellationToken ct = default)
    {
        var qp = new List<string>();
        if (dateFrom.HasValue)
            qp.Add($"from={dateFrom.Value:yyyy-MM-dd}");
        if (dateTo.HasValue)
            qp.Add($"to={dateTo.Value:yyyy-MM-dd}");

        var path = $"/data/v1/accounts/{Uri.EscapeDataString(accountId)}/transactions";
        if (qp.Count > 0)
            path += "?" + string.Join("&", qp);

        var raw = await GetWithBearerAsync<TransactionsResponse>(accessToken, path, ct);
        return (raw.Results ?? []).Select(t => MapTransaction(t, isPending: false)).ToList();
    }

    public async Task<IReadOnlyList<TrueLayerTransaction>> GetPendingTransactionsAsync(
        string accessToken, string accountId, CancellationToken ct = default)
    {
        var raw = await GetWithBearerAsync<TransactionsResponse>(
            accessToken,
            $"/data/v1/accounts/{Uri.EscapeDataString(accountId)}/transactions/pending",
            ct);
        return (raw.Results ?? []).Select(t => MapTransaction(t, isPending: true)).ToList();
    }

    public async Task<IReadOnlyList<TrueLayerAccountInfo>> ListCardsAsync(string accessToken, CancellationToken ct = default)
    {
        var raw = await GetWithBearerAsync<CardsResponse>(accessToken, "/data/v1/cards", ct);
        return (raw.Results ?? []).Select(MapCard).ToList();
    }

    public async Task<TrueLayerCardBalance?> GetCardBalanceAsync(string accessToken, string cardId, CancellationToken ct = default)
    {
        var raw = await GetWithBearerAsync<CardBalanceResponse>(
            accessToken, $"/data/v1/cards/{Uri.EscapeDataString(cardId)}/balance", ct);

        var entry = raw.Results?.FirstOrDefault();
        if (entry is null)
            return null;

        return new TrueLayerCardBalance(
            Current: entry.Current,
            Available: entry.Available ?? entry.Current,
            CreditLimit: entry.CreditLimit,
            Currency: entry.Currency ?? "EUR");
    }

    public async Task<IReadOnlyList<TrueLayerTransaction>> GetCardTransactionsAsync(
        string accessToken, string cardId, DateOnly? dateFrom, DateOnly? dateTo, CancellationToken ct = default)
    {
        var qp = new List<string>();
        if (dateFrom.HasValue)
            qp.Add($"from={dateFrom.Value:yyyy-MM-dd}");
        if (dateTo.HasValue)
            qp.Add($"to={dateTo.Value:yyyy-MM-dd}");

        var path = $"/data/v1/cards/{Uri.EscapeDataString(cardId)}/transactions";
        if (qp.Count > 0)
            path += "?" + string.Join("&", qp);

        var raw = await GetWithBearerAsync<TransactionsResponse>(accessToken, path, ct);
        return (raw.Results ?? []).Select(t => MapTransaction(t, isPending: false)).ToList();
    }

    public async Task<IReadOnlyList<TrueLayerTransaction>> GetCardPendingTransactionsAsync(
        string accessToken, string cardId, CancellationToken ct = default)
    {
        var raw = await GetWithBearerAsync<TransactionsResponse>(
            accessToken,
            $"/data/v1/cards/{Uri.EscapeDataString(cardId)}/transactions/pending",
            ct);
        return (raw.Results ?? []).Select(t => MapTransaction(t, isPending: true)).ToList();
    }

    private static TrueLayerAccountInfo MapAccount(AccountEntry a) => new(
        AccountId: a.AccountId,
        DisplayName: a.DisplayName ?? a.ProviderDisplayName ?? "Account",
        Currency: a.Currency ?? "EUR",
        ProviderName: a.ProviderDisplayName ?? string.Empty,
        AccountType: MapAccountType(a.AccountType),
        Iban: a.AccountNumber?.Iban,
        AccountNumberLast4: ExtractLast4(a.AccountNumber?.Iban, a.AccountNumber?.Number));

    private static TrueLayerAccountInfo MapCard(CardEntry c) => new(
        AccountId: c.AccountId,
        DisplayName: c.DisplayName ?? c.ProviderDisplayName ?? "Card",
        Currency: c.Currency ?? "EUR",
        ProviderName: c.ProviderDisplayName ?? string.Empty,
        AccountType: "credit",
        Iban: null,
        AccountNumberLast4: ExtractLast4(iban: null, number: c.PartialCardNumber));

    private static TrueLayerTransaction MapTransaction(TransactionEntry t, bool isPending)
    {
        var amount = t.Amount;
        var txType = string.IsNullOrWhiteSpace(t.TransactionType)
            ? (amount < 0 ? "debit" : "credit")
            : t.TransactionType.ToLowerInvariant();

        var description = !string.IsNullOrWhiteSpace(t.Description)
            ? t.Description
            : t.MerchantName ?? "Transaction";

        return new TrueLayerTransaction(
            TransactionId: t.TransactionId ?? Guid.NewGuid().ToString(),
            Timestamp: ParseTimestamp(t.Timestamp),
            Amount: amount,
            Currency: t.Currency ?? "EUR",
            Description: description.Trim(),
            MerchantName: t.MerchantName,
            TransactionType: txType,
            IsPending: isPending,
            Classification: t.TransactionClassification ?? []);
    }

    private static DateTime ParseTimestamp(string? raw)
        => DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt
            : DateTime.UtcNow;

    private static string MapAccountType(string? truelayerType)
    {
        var s = (truelayerType ?? string.Empty).ToUpperInvariant();
        return s switch
        {
            "TRANSACTION" or "CURRENT" => "checking",
            "SAVINGS" => "savings",
            "CREDIT_CARD" or "CREDIT" => "credit",
            _ => "checking"
        };
    }

    internal static string ExtractLast4(string? iban, string? number)
    {
        var source = !string.IsNullOrWhiteSpace(iban) ? iban : number;
        if (string.IsNullOrWhiteSpace(source))
            return "0000";
        var digits = new string([.. source.Where(char.IsDigit)]);
        return digits.Length >= 4 ? digits[^4..] : "0000";
    }

    private async Task<TrueLayerTokenSet> PostTokenAsync(Dictionary<string, string> body, CancellationToken ct)
    {
        var auth = httpClientFactory.CreateClient(AuthClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(body)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await auth.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct);
            throw new TrueLayerException(
                ErrorCodeForStatus(response.StatusCode),
                $"TrueLayer token endpoint error ({(int)response.StatusCode}): {errBody}",
                (int)response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOpts, ct)
            ?? throw new TrueLayerException("TRUELAYER_PARSE_ERROR", "Empty token response.");

        return new TrueLayerTokenSet(
            AccessToken: payload.AccessToken ?? string.Empty,
            RefreshToken: payload.RefreshToken ?? string.Empty,
            ExpiresInSeconds: payload.ExpiresIn);
    }

    private async Task<T> GetWithBearerAsync<T>(string accessToken, string path, CancellationToken ct)
    {
        var api = httpClientFactory.CreateClient(ApiClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await api.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new TrueLayerException(
                ErrorCodeForStatus(response.StatusCode),
                $"TrueLayer API error ({(int)response.StatusCode}) on {path}: {body}",
                (int)response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOpts, ct);
        return payload ?? throw new TrueLayerException("TRUELAYER_PARSE_ERROR", $"Empty body from {path}");
    }

    private static string ErrorCodeForStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "TRUELAYER_UNAUTHORIZED",
        HttpStatusCode.Forbidden => "TRUELAYER_FORBIDDEN",
        HttpStatusCode.NotFound => "TRUELAYER_NOT_FOUND",
        HttpStatusCode.TooManyRequests => "TRUELAYER_RATE_LIMITED",
        HttpStatusCode.BadRequest => "TRUELAYER_BAD_REQUEST",
        _ => "TRUELAYER_ERROR"
    };

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
        {
            logger.LogWarning("TrueLayer credentials are not configured.");
            throw new TrueLayerException(
                "TRUELAYER_NOT_CONFIGURED",
                "TrueLayer credentials are not configured. Set TrueLayer:ClientId and TrueLayer:ClientSecret.",
                503);
        }
    }

    private sealed class ProviderEntry
    {
        [JsonPropertyName("provider_id")] public string ProviderId { get; set; } = string.Empty;
        [JsonPropertyName("display_name")] public string DisplayName { get; set; } = string.Empty;
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("logo_url")] public string? LogoUrl { get; set; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    }

    private sealed class AccountsResponse
    {
        [JsonPropertyName("results")] public List<AccountEntry>? Results { get; set; }
    }

    private sealed class AccountEntry
    {
        [JsonPropertyName("account_id")] public string AccountId { get; set; } = string.Empty;
        [JsonPropertyName("account_type")] public string? AccountType { get; set; }
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
        [JsonPropertyName("provider")] public ProviderRef? Provider { get; set; }
        [JsonPropertyName("account_number")] public AccountNumber? AccountNumber { get; set; }

        public string? ProviderDisplayName => Provider?.DisplayName;
    }

    private sealed class ProviderRef
    {
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    }

    private sealed class AccountNumber
    {
        [JsonPropertyName("iban")] public string? Iban { get; set; }
        [JsonPropertyName("number")] public string? Number { get; set; }
        [JsonPropertyName("sort_code")] public string? SortCode { get; set; }
    }

    private sealed class CardsResponse
    {
        [JsonPropertyName("results")] public List<CardEntry>? Results { get; set; }
    }

    private sealed class CardEntry
    {
        [JsonPropertyName("account_id")] public string AccountId { get; set; } = string.Empty;
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
        [JsonPropertyName("partial_card_number")] public string? PartialCardNumber { get; set; }
        [JsonPropertyName("provider")] public ProviderRef? Provider { get; set; }

        public string? ProviderDisplayName => Provider?.DisplayName;
    }

    private sealed class BalanceResponse
    {
        [JsonPropertyName("results")] public List<BalanceEntry>? Results { get; set; }
    }

    private sealed class CardBalanceResponse
    {
        [JsonPropertyName("results")] public List<CardBalanceEntry>? Results { get; set; }
    }

    private sealed class CardBalanceEntry
    {
        [JsonPropertyName("current")] public decimal Current { get; set; }
        [JsonPropertyName("available")] public decimal? Available { get; set; }
        [JsonPropertyName("credit_limit")] public decimal? CreditLimit { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
    }

    private sealed class BalanceEntry
    {
        [JsonPropertyName("current")] public decimal Current { get; set; }
        [JsonPropertyName("available")] public decimal? Available { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
    }

    private sealed class TransactionsResponse
    {
        [JsonPropertyName("results")] public List<TransactionEntry>? Results { get; set; }
    }

    private sealed class TransactionEntry
    {
        [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
        [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }
        [JsonPropertyName("amount")] public decimal Amount { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("merchant_name")] public string? MerchantName { get; set; }
        [JsonPropertyName("transaction_type")] public string? TransactionType { get; set; }
        [JsonPropertyName("transaction_classification")] public List<string>? TransactionClassification { get; set; }
    }
}
