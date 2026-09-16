namespace FinanceSentry.Modules.CryptoSync.Domain.Interfaces;

/// <summary>
/// One crypto venue behind a provider-agnostic seam (#472). Every venue authenticates with an API
/// key plus a secret; what the secret is belongs to the adapter (Binance: HMAC API secret,
/// Revolut X: Ed25519 private key PEM).
/// </summary>
public interface ICryptoExchangeAdapter
{
    /// <summary>The provider slug — see <c>CryptoExchangeProvider</c>.</summary>
    string ExchangeName { get; }

    /// <summary>Throws a <c>CryptoExchangeException</c> when the venue rejects the credential.</summary>
    Task ValidateCredentialsAsync(string apiKey, string apiSecret, CancellationToken ct = default);

    /// <summary>
    /// Current per-asset holdings, <see cref="CryptoAssetBalance.UsdValue"/> already converted to
    /// USD at this boundary — the adapter is the last place the quote currency is in scope.
    /// </summary>
    Task<IReadOnlyList<CryptoAssetBalance>> GetHoldingsAsync(
        string apiKey,
        string apiSecret,
        CancellationToken ct = default);

    /// <summary>
    /// The user's fills for <paramref name="asset"/> after <paramref name="cursor"/>, plus the cursor
    /// to resume from next time. The cursor is opaque to callers: each venue pages differently
    /// (Binance by numeric trade id, Revolut X by time window), so only the adapter that produced it
    /// interprets it. A null cursor means "never walked". Trades already covered by the cursor are
    /// never returned again.
    /// </summary>
    Task<CryptoTradePage> GetTradesAsync(
        string apiKey,
        string apiSecret,
        string asset,
        string? cursor,
        CancellationToken ct = default);
}

public sealed record CryptoAssetBalance(
    string Asset,
    decimal FreeQuantity,
    decimal LockedQuantity,
    decimal UsdValue);

public sealed record CryptoTradePage(
    IReadOnlyList<CryptoTrade> Trades,
    string? NextCursor);

public sealed record CryptoTrade(
    string TradeId,
    string Asset,
    string QuoteAsset,
    decimal Quantity,
    decimal PriceUsd,
    decimal QuoteQuantityUsd,
    bool IsBuyer,
    DateTime Timestamp);
