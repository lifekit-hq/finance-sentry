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

/// <summary><c>GET /transactions</c>.</summary>
public sealed record RevolutXTransactionsResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<RevolutXTransaction> Data,
    [property: JsonPropertyName("metadata")] RevolutXPageMetadata? Metadata);

/// <summary>
/// One account transaction. <see cref="Source"/> is the leg debited, <see cref="Destination"/> the leg credited, each
/// with its net amount and the type of the account it touched (<c>revolut</c>, <c>revolut_x</c>,
/// <c>external_bank_account</c>, <c>external_crypto_wallet</c>). <see cref="ProcessedDate"/> is absent while pending.
/// </summary>
public sealed record RevolutXTransaction(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("source")] RevolutXTransactionLeg? Source,
    [property: JsonPropertyName("destination")] RevolutXTransactionLeg? Destination,
    [property: JsonPropertyName("created_date")] long CreatedDateMs,
    [property: JsonPropertyName("processed_date")] long? ProcessedDateMs);

public sealed record RevolutXTransactionLeg(
    [property: JsonPropertyName("amount")] string? Amount,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("account")] RevolutXLegAccount? Account);

public sealed record RevolutXLegAccount(
    [property: JsonPropertyName("type")] string? Type);

/// <summary>
/// <c>GET /transactions/{id}</c>. Each leg may carry the fee the venue kept (on the source leg when both legs share a
/// currency; for an external transfer only Revolut's own fee, not the network's). <see cref="CryptoTransactionHash"/>
/// and <see cref="Network"/> are present for sends to and receives from external crypto wallets.
/// </summary>
public sealed record RevolutXTransactionDetails(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("source")] RevolutXTransactionDetailLeg? Source,
    [property: JsonPropertyName("destination")] RevolutXTransactionDetailLeg? Destination,
    [property: JsonPropertyName("crypto_transaction_hash")] string? CryptoTransactionHash,
    [property: JsonPropertyName("network")] string? Network);

public sealed record RevolutXTransactionDetailLeg(
    [property: JsonPropertyName("amount")] string? Amount,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("fee")] string? Fee,
    [property: JsonPropertyName("fee_currency")] string? FeeCurrency);

public sealed record RevolutXErrorResponse(
    [property: JsonPropertyName("error_id")] string? ErrorId,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("timestamp")] long? Timestamp);
