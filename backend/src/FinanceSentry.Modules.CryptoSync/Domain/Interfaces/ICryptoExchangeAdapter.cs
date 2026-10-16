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

    /// <summary>
    /// True when the venue cannot serve fills from before the user connected (Revolut X: one-week
    /// windows, no backfill), so cost basis is accumulated forward from the connect date by
    /// <c>ForwardCostBasisLedger</c> and lots the venue never showed stay unpriced. False when the
    /// full history is walkable (Binance: by trade id).
    /// </summary>
    bool TradeHistoryStartsAtConnect { get; }

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
    /// never returned again. <paramref name="walk"/> bounds a time-paged walk; a venue that pages by
    /// id ignores it.
    /// </summary>
    Task<CryptoTradePage> GetTradesAsync(
        string apiKey,
        string apiSecret,
        string asset,
        string? cursor,
        CryptoTradeWalk walk,
        CancellationToken ct = default);

    /// <summary>
    /// The completed movements across the venue's edge (S2a: fiat legs, and Revolut X transfers) created from
    /// <see cref="CryptoFlowWalk.From"/> to <see cref="CryptoFlowWalk.AsOf"/>, raw: the adapter never decides
    /// whether a movement is external or internal. <see cref="CryptoFlowPage.WalkedTo"/> is where the next walk
    /// resumes: before the earliest movement still pending, so it is read again once it settles, and before
    /// <see cref="CryptoFlowWalk.AsOf"/> when one run walks only part of the gap. A venue that reads one source per run
    /// (Binance) takes <see cref="CryptoFlowWalk.Lane"/> and returns the lane to read next in
    /// <see cref="CryptoFlowPage.NextLane"/>, moving <see cref="CryptoFlowPage.WalkedTo"/> only once every lane has
    /// read the window. Any call failing throws.
    /// </summary>
    Task<CryptoFlowPage> GetFlowsAsync(
        string apiKey,
        string apiSecret,
        CryptoFlowWalk walk,
        CancellationToken ct = default);

    /// <summary>
    /// Whether the venue's on-chain transfers are read by a walk of their own (<see cref="GetChainFlowsAsync"/>, on
    /// the credential's <c>ChainFlowsWalkedTo</c> cursor) rather than inside <see cref="GetFlowsAsync"/>.
    /// </summary>
    bool WalksChainFlows => false;

    /// <summary>
    /// The completed on-chain deposits and withdrawals (S2b) from <see cref="CryptoFlowWalk.From"/>, raw, with the
    /// same resume rule as <see cref="GetFlowsAsync"/>: <see cref="CryptoFlowPage.WalkedTo"/> stops before the
    /// earliest movement still pending. Only called when <see cref="WalksChainFlows"/>.
    /// </summary>
    Task<CryptoFlowPage> GetChainFlowsAsync(
        string apiKey,
        string apiSecret,
        CryptoFlowWalk walk,
        CancellationToken ct = default) => Task.FromResult(new CryptoFlowPage([], walk.AsOf));
}

/// <param name="IsFiat">
/// Fiat cash held on the venue (Revolut X EUR/USD balances): its quantity is the native amount in
/// <paramref name="Asset"/>. Venue cash — neither a crypto position nor bank cash.
/// </param>
public sealed record CryptoAssetBalance(
    string Asset,
    decimal FreeQuantity,
    decimal LockedQuantity,
    decimal UsdValue,
    bool IsFiat = false);

/// <param name="TrackedSince">When the venue was connected: a never-walked cursor starts here.</param>
/// <param name="AsOf">
/// Fills after this instant are left for the next walk. The sync takes it just before reading
/// balances, so the fills returned are the ones the balance snapshot already reflects.
/// </param>
public sealed record CryptoTradeWalk(DateTime TrackedSince, DateTime AsOf);

/// <param name="IsComplete">
/// False when the walk stopped short of <see cref="CryptoTradeWalk.AsOf"/> (a long outage left more
/// windows than one run walks); <paramref name="NextCursor"/> then resumes where it stopped.
/// </param>
public sealed record CryptoTradePage(
    IReadOnlyList<CryptoTrade> Trades,
    string? NextCursor,
    bool IsComplete = true);

public sealed record CryptoTrade(
    string TradeId,
    string Asset,
    string QuoteAsset,
    decimal Quantity,
    decimal PriceUsd,
    decimal QuoteQuantityUsd,
    bool IsBuyer,
    DateTime Timestamp);

public sealed record CryptoFlowWalk(DateTime From, DateTime AsOf, int Lane = 0);

public sealed record CryptoFlowPage(IReadOnlyList<CryptoFlow> Flows, DateTime WalkedTo, int NextLane = 0);

/// <summary>One completed movement as the venue reported it. See <c>CryptoFlowEvent</c>.</summary>
/// <param name="ProviderType">A <c>CryptoFlowTypes</c> value.</param>
/// <param name="Amount">
/// Positive, in <paramref name="Unit"/>, on the far side of the venue's edge: what the bank or card paid in or
/// received, what the sending or receiving account moved. A fee the venue kept is <paramref name="Fee"/>, not netted.
/// </param>
/// <param name="UnitPriceUsd">USD per unit at read time for a crypto unit; null for fiat (dated FX values it) or when unpriced.</param>
/// <param name="CounterpartyRef">The venue's raw word for the other side: a Revolut X account type, a Binance method.</param>
/// <param name="TxRef">The chain transaction id (hash) as the venue reported it; null where none is given.</param>
/// <param name="Network">The chain, in the venue's own vocabulary; null where none is given.</param>
public sealed record CryptoFlow(
    string EventId,
    string ProviderType,
    DateTime OccurredAt,
    decimal Amount,
    string Unit,
    bool IsFiat,
    decimal? UnitPriceUsd,
    decimal? Fee,
    string? FeeUnit,
    string? CounterpartyRef,
    string Status,
    string? TxRef = null,
    string? Network = null);
