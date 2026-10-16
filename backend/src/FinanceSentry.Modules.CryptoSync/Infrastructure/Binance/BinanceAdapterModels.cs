using System.Text.Json.Serialization;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;

public sealed record BinanceAccountResponse(
    [property: JsonPropertyName("balances")] IReadOnlyList<BinanceBalance> Balances);

public sealed record BinanceBalance(
    [property: JsonPropertyName("asset")] string Asset,
    [property: JsonPropertyName("free")] string Free,
    [property: JsonPropertyName("locked")] string Locked);

public sealed record BinancePriceTicker(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("price")] string Price);

public sealed record BinanceErrorResponse(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("msg")] string Message);

// /sapi/v1/asset/get-funding-asset — flat list of per-asset balances in the Funding wallet.
public sealed record BinanceFundingAsset(
    [property: JsonPropertyName("asset")] string Asset,
    [property: JsonPropertyName("free")] string Free,
    [property: JsonPropertyName("locked")] string? Locked,
    [property: JsonPropertyName("freeze")] string? Freeze,
    [property: JsonPropertyName("withdrawing")] string? Withdrawing);

// Generic paginated wrapper for the Simple Earn endpoints.
public sealed record BinanceEarnPage<T>(
    [property: JsonPropertyName("rows")] IReadOnlyList<T> Rows,
    [property: JsonPropertyName("total")] int Total);

// /sapi/v1/simple-earn/flexible/position — Simple Earn Flexible product positions.
public sealed record BinanceFlexibleEarnPosition(
    [property: JsonPropertyName("asset")] string Asset,
    [property: JsonPropertyName("totalAmount")] string TotalAmount);

// /sapi/v1/simple-earn/locked/position — Simple Earn Locked product positions.
public sealed record BinanceLockedEarnPosition(
    [property: JsonPropertyName("asset")] string Asset,
    [property: JsonPropertyName("amount")] string Amount);

// /api/v3/myTrades — per-symbol trade history (one row per fill).
public sealed record BinanceTradeRow(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("price")] string Price,
    [property: JsonPropertyName("qty")] string Quantity,
    [property: JsonPropertyName("quoteQty")] string QuoteQuantity,
    [property: JsonPropertyName("commission")] string Commission,
    [property: JsonPropertyName("commissionAsset")] string CommissionAsset,
    [property: JsonPropertyName("time")] long TimeMs,
    [property: JsonPropertyName("isBuyer")] bool IsBuyer);

// /sapi/v1/fiat/orders and /sapi/v1/fiat/payments share one envelope; code "000000" is success.
public sealed record BinanceFiatPage<T>(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("data")] IReadOnlyList<T>? Data,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("success")] bool Success);

// /sapi/v1/fiat/orders — a fiat deposit (transactionType 0) or withdrawal (1). indicatedAmount is the trade amount,
// amount what was credited or paid out after totalFee.
public sealed record BinanceFiatOrder(
    [property: JsonPropertyName("orderNo")] string OrderNo,
    [property: JsonPropertyName("fiatCurrency")] string? FiatCurrency,
    [property: JsonPropertyName("indicatedAmount")] string? IndicatedAmount,
    [property: JsonPropertyName("amount")] string? Amount,
    [property: JsonPropertyName("totalFee")] string? TotalFee,
    [property: JsonPropertyName("method")] string? Method,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("createTime")] long CreateTimeMs,
    [property: JsonPropertyName("updateTime")] long? UpdateTimeMs);

// /sapi/v1/fiat/payments — crypto bought (transactionType 0) or sold (1) for fiat: sourceAmount of fiatCurrency
// for obtainAmount of cryptoCurrency. paymentMethod is reported on buys only.
public sealed record BinanceFiatPayment(
    [property: JsonPropertyName("orderNo")] string OrderNo,
    [property: JsonPropertyName("sourceAmount")] string? SourceAmount,
    [property: JsonPropertyName("fiatCurrency")] string? FiatCurrency,
    [property: JsonPropertyName("obtainAmount")] string? ObtainAmount,
    [property: JsonPropertyName("cryptoCurrency")] string? CryptoCurrency,
    [property: JsonPropertyName("totalFee")] string? TotalFee,
    [property: JsonPropertyName("price")] string? Price,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("paymentMethod")] string? PaymentMethod,
    [property: JsonPropertyName("createTime")] long CreateTimeMs,
    [property: JsonPropertyName("updateTime")] long? UpdateTimeMs);
