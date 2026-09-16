using System.Text.Json.Serialization;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;

// Revolut X returns every monetary value as a string to keep precision; parse with the invariant
// culture into decimal, never through double.

/// <summary><c>GET /balances</c> — one entry per currency, crypto and fiat alike.</summary>
public sealed record RevolutXBalance(
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("available")] string? Available,
    [property: JsonPropertyName("reserved")] string? Reserved,
    [property: JsonPropertyName("staked")] string? Staked,
    [property: JsonPropertyName("total")] string? Total);

/// <summary><c>GET /configuration/currencies</c> — a map keyed by currency code.</summary>
public sealed record RevolutXCurrency(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("scale")] int Scale,
    [property: JsonPropertyName("asset_type")] string? AssetType,
    [property: JsonPropertyName("status")] string? Status);

/// <summary><c>GET /tickers</c>.</summary>
public sealed record RevolutXTickersResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<RevolutXTicker> Data);

/// <summary>One pair's latest prices; <see cref="Symbol"/> uses the slash form (<c>BTC/USD</c>).</summary>
public sealed record RevolutXTicker(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("bid")] string? Bid,
    [property: JsonPropertyName("ask")] string? Ask,
    [property: JsonPropertyName("mid")] string? Mid,
    [property: JsonPropertyName("last_price")] string? LastPrice);

public sealed record RevolutXErrorResponse(
    [property: JsonPropertyName("error_id")] string? ErrorId,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("timestamp")] long? Timestamp);
