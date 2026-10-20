using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;

public sealed class BinanceHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly int _recvWindowMs;

    public BinanceHttpClient(HttpClient httpClient, IOptions<BinanceOptions> options)
    {
        _httpClient = httpClient;
        _baseUrl = options.Value.BaseUrl;
        _recvWindowMs = options.Value.RecvWindowMs;
    }

    // Spot wallet — retained for credential validation. Use the consolidated
    // helpers below (Funding, Earn) to compute the user's true holdings.
    public Task<BinanceAccountResponse> GetAccountAsync(
        string apiKey,
        string apiSecret,
        CancellationToken ct = default) =>
        SendSignedAsync<BinanceAccountResponse>(
            HttpMethod.Get, "/api/v3/account", apiKey, apiSecret, queryParams: string.Empty, ct);

    public async Task<IReadOnlyList<BinanceFundingAsset>> GetFundingAssetsAsync(
        string apiKey,
        string apiSecret,
        CancellationToken ct = default) =>
        await SendSignedAsync<IReadOnlyList<BinanceFundingAsset>>(
            HttpMethod.Post, "/sapi/v1/asset/get-funding-asset", apiKey, apiSecret, queryParams: string.Empty, ct);

    public Task<BinanceEarnPage<BinanceFlexibleEarnPosition>> GetFlexibleEarnPositionsAsync(
        string apiKey,
        string apiSecret,
        CancellationToken ct = default) =>
        SendSignedAsync<BinanceEarnPage<BinanceFlexibleEarnPosition>>(
            HttpMethod.Get, "/sapi/v1/simple-earn/flexible/position", apiKey, apiSecret,
            queryParams: "size=100&current=1", ct);

    public Task<BinanceEarnPage<BinanceLockedEarnPosition>> GetLockedEarnPositionsAsync(
        string apiKey,
        string apiSecret,
        CancellationToken ct = default) =>
        SendSignedAsync<BinanceEarnPage<BinanceLockedEarnPosition>>(
            HttpMethod.Get, "/sapi/v1/simple-earn/locked/position", apiKey, apiSecret,
            queryParams: "size=100&current=1", ct);

    public async Task<IReadOnlyList<BinancePriceTicker>> GetAllPricesAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/v3/ticker/price", ct);
        return await DeserializeAsync<IReadOnlyList<BinancePriceTicker>>(response, ct);
    }

    // /api/v3/klines — public market data (no key): the daily candle that opens at or after startTime (a UTC midnight). Its close
    // is the price on that date, which is how a fee paid in another asset is valued on the fill's own date.
    // https://developers.binance.com/docs/binance-spot-api-docs/rest-api/market-data-endpoints#klinecandlestick-data
    public async Task<decimal?> GetDailyClosePriceAsync(string symbol, long startTimeMs, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(
            $"{_baseUrl}/api/v3/klines?symbol={symbol}&interval=1d&startTime={startTimeMs}&limit=1", ct);
        var candles = await DeserializeAsync<JsonElement>(response, ct);
        if (candles.ValueKind != JsonValueKind.Array || candles.GetArrayLength() == 0)
        {
            return null;
        }

        var close = candles[0].ValueKind == JsonValueKind.Array && candles[0].GetArrayLength() > 4 ? candles[0][4].GetString() : null;
        return decimal.TryParse(close, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var price)
            && price > 0m
            ? price
            : null;
    }

    // /api/v3/myTrades — returns up to 1000 fills for a given symbol. Pass fromId to walk
    // forward through trade history incrementally. Binance docs:
    // https://developers.binance.com/docs/binance-spot-api-docs/rest-api/account-endpoints#account-trade-list-user_data
    public Task<IReadOnlyList<BinanceTradeRow>> GetMyTradesAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        long fromId,
        int limit,
        CancellationToken ct = default)
    {
        var query = fromId > 0
            ? $"symbol={symbol}&fromId={fromId}&limit={limit}"
            : $"symbol={symbol}&limit={limit}";
        return SendSignedAsync<IReadOnlyList<BinanceTradeRow>>(
            HttpMethod.Get, "/api/v3/myTrades", apiKey, apiSecret, query, ct);
    }

    // /sapi/v1/fiat/orders — fiat deposits (transactionType 0) or withdrawals (1) created in [beginTime, endTime].
    // https://developers.binance.com/docs/fiat/rest-api/Get-Fiat-Deposit-Withdraw-History
    public Task<BinanceFiatPage<BinanceFiatOrder>> GetFiatOrdersAsync(
        string apiKey,
        string apiSecret,
        int transactionType,
        long beginTimeMs,
        long endTimeMs,
        int page,
        int rows,
        CancellationToken ct = default) =>
        SendSignedAsync<BinanceFiatPage<BinanceFiatOrder>>(
            HttpMethod.Get, "/sapi/v1/fiat/orders", apiKey, apiSecret,
            FiatHistoryQuery(transactionType, beginTimeMs, endTimeMs, page, rows), ct);

    // /sapi/v1/fiat/payments — crypto bought (transactionType 0) or sold (1) for fiat, created in [beginTime, endTime].
    // https://developers.binance.com/docs/fiat/rest-api/Get-Fiat-Payments-History
    public Task<BinanceFiatPage<BinanceFiatPayment>> GetFiatPaymentsAsync(
        string apiKey,
        string apiSecret,
        int transactionType,
        long beginTimeMs,
        long endTimeMs,
        int page,
        int rows,
        CancellationToken ct = default) =>
        SendSignedAsync<BinanceFiatPage<BinanceFiatPayment>>(
            HttpMethod.Get, "/sapi/v1/fiat/payments", apiKey, apiSecret,
            FiatHistoryQuery(transactionType, beginTimeMs, endTimeMs, page, rows), ct);

    // /sapi/v1/capital/deposit/hisrec — coin deposits inserted in [startTime, endTime] (at most 90 days apart), every
    // status, paged by offset. https://developers.binance.com/docs/wallet/capital/deposite-history
    public Task<IReadOnlyList<BinanceCoinDeposit>> GetCoinDepositsAsync(
        string apiKey,
        string apiSecret,
        long startTimeMs,
        long endTimeMs,
        int offset,
        int limit,
        CancellationToken ct = default) =>
        SendSignedAsync<IReadOnlyList<BinanceCoinDeposit>>(
            HttpMethod.Get, "/sapi/v1/capital/deposit/hisrec", apiKey, apiSecret,
            CoinHistoryQuery(startTimeMs, endTimeMs, offset, limit), ct);

    // /sapi/v1/capital/withdraw/history — coin withdrawals applied in [startTime, endTime] (at most 90 days apart),
    // every status, paged by offset. https://developers.binance.com/docs/wallet/capital/withdraw-history
    public Task<IReadOnlyList<BinanceCoinWithdrawal>> GetCoinWithdrawalsAsync(
        string apiKey,
        string apiSecret,
        long startTimeMs,
        long endTimeMs,
        int offset,
        int limit,
        CancellationToken ct = default) =>
        SendSignedAsync<IReadOnlyList<BinanceCoinWithdrawal>>(
            HttpMethod.Get, "/sapi/v1/capital/withdraw/history", apiKey, apiSecret,
            CoinHistoryQuery(startTimeMs, endTimeMs, offset, limit), ct);

    private static string CoinHistoryQuery(long startTimeMs, long endTimeMs, int offset, int limit) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"startTime={startTimeMs}&endTime={endTimeMs}&offset={offset}&limit={limit}");

    // The earn, distribution and dust history of a day-long window: weighted far below fiat/orders, so the last lane
    // reads them together.
    // https://developers.binance.com/docs/simple_earn/history/Get-Flexible-Rewards-History
    public Task<BinanceEarnPage<BinanceFlexibleReward>> GetFlexibleRewardsAsync(
        string apiKey, string apiSecret, string type, long beginTimeMs, long endTimeMs, int page, int size, CancellationToken ct = default) =>
        SendSignedAsync<BinanceEarnPage<BinanceFlexibleReward>>(
            HttpMethod.Get, "/sapi/v1/simple-earn/flexible/history/rewardsRecord", apiKey, apiSecret,
            string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"type={type}&startTime={beginTimeMs}&endTime={endTimeMs}&current={page}&size={size}"), ct);

    // https://developers.binance.com/docs/simple_earn/history/Get-Locked-Rewards-History
    public Task<BinanceEarnPage<BinanceLockedReward>> GetLockedRewardsAsync(
        string apiKey, string apiSecret, long beginTimeMs, long endTimeMs, int page, int size, CancellationToken ct = default) =>
        SendSignedAsync<BinanceEarnPage<BinanceLockedReward>>(
            HttpMethod.Get, "/sapi/v1/simple-earn/locked/history/rewardsRecord", apiKey, apiSecret,
            string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"startTime={beginTimeMs}&endTime={endTimeMs}&current={page}&size={size}"), ct);

    // https://developers.binance.com/docs/wallet/asset/assets-divided-record
    public Task<BinanceAssetDividendPage> GetAssetDividendsAsync(
        string apiKey, string apiSecret, long beginTimeMs, long endTimeMs, int limit, CancellationToken ct = default) =>
        SendSignedAsync<BinanceAssetDividendPage>(
            HttpMethod.Get, "/sapi/v1/asset/assetDividend", apiKey, apiSecret,
            string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"startTime={beginTimeMs}&endTime={endTimeMs}&limit={limit}"), ct);

    // https://developers.binance.com/docs/wallet/asset/dust-log
    public Task<BinanceDribbletResponse> GetDribbletAsync(
        string apiKey, string apiSecret, long beginTimeMs, long endTimeMs, CancellationToken ct = default) =>
        SendSignedAsync<BinanceDribbletResponse>(
            HttpMethod.Get, "/sapi/v1/asset/dribblet", apiKey, apiSecret,
            string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"startTime={beginTimeMs}&endTime={endTimeMs}"), ct);

    private static string FiatHistoryQuery(int transactionType, long beginTimeMs, long endTimeMs, int page, int rows) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"transactionType={transactionType}&beginTime={beginTimeMs}&endTime={endTimeMs}&page={page}&rows={rows}");

    private async Task<T> SendSignedAsync<T>(
        HttpMethod method,
        string path,
        string apiKey,
        string apiSecret,
        string queryParams,
        CancellationToken ct)
    {
        var prefix = string.IsNullOrEmpty(queryParams) ? string.Empty : $"{queryParams}&";
        var query = BuildSignedQuery(apiSecret, $"{prefix}recvWindow={_recvWindowMs}");
        var request = new HttpRequestMessage(method, $"{_baseUrl}{path}?{query}");
        request.Headers.Add("X-MBX-APIKEY", apiKey);

        var response = await _httpClient.SendAsync(request, ct);
        return await DeserializeAsync<T>(response, ct);
    }

    private static string BuildSignedQuery(string apiSecret, string queryParams)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = $"{queryParams}&timestamp={timestamp}";
        var signature = ComputeHmacSha256(apiSecret, payload);
        return $"{payload}&signature={signature}";
    }

    private static string ComputeHmacSha256(string secret, string data)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var hash = HMACSHA256.HashData(keyBytes, dataBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            BinanceErrorResponse? error = null;
            try { error = JsonSerializer.Deserialize<BinanceErrorResponse>(body); }
            catch (JsonException) { /* fall through with null */ }
            throw new BinanceException(
                error?.Message ?? $"Binance API error: HTTP {(int)response.StatusCode}",
                error?.Code,
                (int)response.StatusCode);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body)
                ?? throw new BinanceException("Binance API returned empty response.");
        }
        catch (JsonException ex)
        {
            throw new BinanceException("Binance API returned an unreadable response.", ex);
        }
    }
}
