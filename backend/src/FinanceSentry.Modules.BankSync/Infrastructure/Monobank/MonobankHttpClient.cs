namespace FinanceSentry.Modules.BankSync.Infrastructure.Monobank;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceSentry.Infrastructure.Http;

public class MonobankHttpClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Dictionary<int, string> CurrencyMap = new()
    {
        { 980, "UAH" }, { 840, "USD" }, { 978, "EUR" },
        { 826, "GBP" }, { 985, "PLN" }, { 756, "CHF" },
        { 203, "CZK" }
    };

    public static string MapCurrency(int numericCode)
        => CurrencyMap.TryGetValue(numericCode, out var code) ? code : $"UNKNOWN_{numericCode}";

    public static decimal KopecksToDecimal(long amount) => amount / 100m;

    /// <summary>
    /// Monobank's client-info <c>balance</c> includes the credit limit on credit-enabled
    /// cards (balance = own funds + creditLimit). Converts to the stored convention:
    /// amount owed (positive = debt) for credit accounts, own funds for everything else.
    /// </summary>
    public static decimal ToStoredBalance(long balance, long creditLimit)
        => KopecksToDecimal(creditLimit > 0 ? creditLimit - balance : balance);

    public async Task<MonobankClientInfo> GetClientInfoAsync(string token, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/personal/client-info");
        request.Headers.Add("X-Token", token);

        using var response = await SendAsync(request, ct);
        var raw = await response.Content.ReadFromJsonAsync<ClientInfoResponse>(JsonOpts, ct)
            ?? throw new MonobankException("MONOBANK_PARSE_ERROR", "Failed to parse client info response.");

        var accounts = raw.Accounts.Select(a => new MonobankAccountInfo(
            Id: a.Id,
            Name: a.Type + " " + MapCurrency(a.CurrencyCode),
            // Any card with a credit line is a liability account, whatever the product
            // (black/platinum/… cards can all carry one) — the product-name map alone
            // would count it as a checking asset.
            Type: a.CreditLimit > 0 ? "credit" : MapAccountType(a.Type),
            MaskedPan: a.MaskedPan?.LastOrDefault() ?? "0000",
            CurrencyCode: a.CurrencyCode,
            Balance: a.Balance,
            ProductType: a.Type,
            CreditLimit: a.CreditLimit)).ToList();

        return new MonobankClientInfo(raw.ClientId, raw.Name, accounts);
    }

    public async Task<IReadOnlyList<MonobankTransaction>> GetStatementsAsync(
        string token, string accountId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var fromUnix = from.ToUnixTimeSeconds();
        var toUnix = to.ToUnixTimeSeconds();

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/personal/statement/{accountId}/{fromUnix}/{toUnix}");
        request.Headers.Add("X-Token", token);

        using var response = await SendAsync(request, ct);
        var raw = await response.Content.ReadFromJsonAsync<List<StatementEntry>>(JsonOpts, ct) ?? [];

        return raw.Select(e => new MonobankTransaction(
            Id: e.Id,
            Time: e.Time,
            Description: e.Description ?? string.Empty,
            MCC: e.MCC,
            Hold: e.Hold,
            Amount: e.Amount,
            CurrencyCode: e.CurrencyCode,
            OperationAmount: e.OperationAmount,
            OperationCurrencyCode: e.OperationCurrencyCode,
            CommissionRate: e.CommissionRate,
            CashbackAmount: e.CashbackAmount,
            Balance: e.Balance,
            Comment: e.Comment,
            ReceiptId: e.ReceiptId,
            InvoiceId: e.InvoiceId,
            CounterName: e.CounterName,
            CounterIban: e.CounterIban)).ToList();
    }

    public async Task SetWebhookAsync(string token, string url, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/personal/webhook");
        request.Headers.Add("X-Token", token);
        request.Content = JsonContent.Create(new { webHookUrl = url });
        using var response = await SendAsync(request, ct);
    }

    private const int MaxErrorBodyLength = 500;
    private const int ServerErrorStatus = 500;

    /// <summary>
    /// Sends once; 429 retries (and their <c>Retry-After</c> waits) belong to the client's
    /// <see cref="MonobankResilience"/> pipeline. Returns a successful response the caller disposes; every
    /// failure path disposes it before throwing.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await http.SendAsync(request, ct);
        try
        {
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                throw new MonobankException("MONOBANK_RATE_LIMITED",
                    "Monobank API rate limit exceeded. Please try again in 60 seconds.", 429)
                {
                    RetryAfter = RetryAfterHeader.WaitFrom(response, DateTimeOffset.UtcNow),
                };

            // Monobank returns 403 (not 401) for a bad/unknown X-Token; treat both as invalid token.
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                throw new MonobankException("MONOBANK_TOKEN_INVALID",
                    "Invalid or expired Monobank token.", 400);

            if (!response.IsSuccessStatusCode)
            {
                // Keep the status and Monobank's own reason: EnsureSuccessStatusCode() threw both away,
                // which left a persistently failing account with no recorded cause.
                var status = (int)response.StatusCode;
                var body = await response.Content.ReadAsStringAsync(ct);
                if (body.Length > MaxErrorBodyLength)
                    body = body[..MaxErrorBodyLength];
                throw new MonobankException(
                    status >= ServerErrorStatus ? "MONOBANK_SERVER_ERROR" : "MONOBANK_HTTP_ERROR",
                    $"Monobank API error ({status}): {body}",
                    status);
            }

            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static string MapAccountType(string monoType) => monoType switch
    {
        "black" or "white" or "platinum" or "iron" or "fop" or "eAid" or "diia" => "checking",
        "yellow" => "credit",
        "savings" => "savings",
        _ => "checking"
    };

    // ── Raw JSON DTOs ─────────────────────────────────────────────────────────

    private sealed class ClientInfoResponse
    {
        [JsonPropertyName("clientId")] public string ClientId { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("accounts")] public List<AccountEntry> Accounts { get; set; } = [];
    }

    private sealed class AccountEntry
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("maskedPan")] public List<string>? MaskedPan { get; set; }
        [JsonPropertyName("currencyCode")] public int CurrencyCode { get; set; }
        [JsonPropertyName("balance")] public long Balance { get; set; }
        [JsonPropertyName("creditLimit")] public long CreditLimit { get; set; }
    }

    private sealed class StatementEntry
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("time")] public long Time { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("mcc")] public int MCC { get; set; }
        [JsonPropertyName("originalMcc")] public int OriginalMCC { get; set; }
        [JsonPropertyName("hold")] public bool Hold { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("currencyCode")] public int CurrencyCode { get; set; }
        [JsonPropertyName("operationAmount")] public long OperationAmount { get; set; }
        [JsonPropertyName("operationCurrencyCode")] public int OperationCurrencyCode { get; set; }
        [JsonPropertyName("commissionRate")] public long CommissionRate { get; set; }
        [JsonPropertyName("cashbackAmount")] public long CashbackAmount { get; set; }
        [JsonPropertyName("balance")] public long Balance { get; set; }
        [JsonPropertyName("comment")] public string? Comment { get; set; }
        [JsonPropertyName("receiptId")] public string? ReceiptId { get; set; }
        [JsonPropertyName("invoiceId")] public string? InvoiceId { get; set; }
        [JsonPropertyName("counterName")] public string? CounterName { get; set; }
        [JsonPropertyName("counterIban")] public string? CounterIban { get; set; }
        [JsonPropertyName("counterEdrpou")] public string? CounterEdrpou { get; set; }
    }
}
