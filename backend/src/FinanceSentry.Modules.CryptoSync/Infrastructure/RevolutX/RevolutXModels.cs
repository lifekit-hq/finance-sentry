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

/// <summary><c>GET /configuration/pairs</c> — one tradable pair.</summary>
public sealed record RevolutXPair(
    [property: JsonPropertyName("base")] string Base,
    [property: JsonPropertyName("quote")] string Quote,
    [property: JsonPropertyName("status")] string? Status);

/// <summary><c>GET /trades/private/{symbol}</c>.</summary>
public sealed record RevolutXTradesResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<RevolutXTrade> Data,
    [property: JsonPropertyName("metadata")] RevolutXPageMetadata? Metadata);

public sealed record RevolutXPageMetadata(
    [property: JsonPropertyName("timestamp")] long? Timestamp,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

/// <summary>
/// One of the user's fills, in the venue's short wire names: <c>q</c> of <c>qc</c> (the base asset)
/// at <c>p</c> <c>pc</c> (the quote) per unit, side <c>s</c> (<c>buy</c>/<c>sell</c>), executed at
/// <c>tdt</c> (Unix ms).
/// </summary>
public sealed record RevolutXTrade(
    [property: JsonPropertyName("tid")] string TradeId,
    [property: JsonPropertyName("p")] string? Price,
    [property: JsonPropertyName("pc")] string? PriceCurrency,
    [property: JsonPropertyName("q")] string? Quantity,
    [property: JsonPropertyName("qc")] string? QuantityCurrency,
    [property: JsonPropertyName("tdt")] long TradeTimestampMs,
    [property: JsonPropertyName("oid")] string? OrderId,
    [property: JsonPropertyName("s")] string? Side);

public sealed record RevolutXErrorResponse(
    [property: JsonPropertyName("error_id")] string? ErrorId,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("timestamp")] long? Timestamp);
