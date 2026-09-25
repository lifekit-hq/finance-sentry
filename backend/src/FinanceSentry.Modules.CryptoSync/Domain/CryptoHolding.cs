namespace FinanceSentry.Modules.CryptoSync.Domain;

/// <summary>
/// One asset held on one venue. Unique on <c>(UserId, Provider, Asset)</c> (#472): BTC on Binance
/// and BTC on Revolut X are two rows, and neither provider's sync touches the other's.
/// </summary>
public sealed class CryptoHolding
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Asset { get; private set; } = string.Empty;
    public decimal FreeQuantity { get; private set; }
    public decimal LockedQuantity { get; private set; }
    public decimal UsdValue { get; private set; }
    public DateTime SyncedAt { get; private set; }
    public string Provider { get; private set; } = string.Empty;

    /// <summary>
    /// Fiat cash held on the venue (#472). Its quantity is the native amount in <see cref="Asset"/>
    /// (the currency code); it has no cost basis and is never a crypto position or bank cash.
    /// </summary>
    public bool IsFiat { get; private set; }

    public decimal? CostBasisUsd { get; private set; }
    public decimal? AverageBuyPriceUsd { get; private set; }
    public decimal? RealizedPnlUsd { get; private set; }
    public DateTime? LastTradeAt { get; private set; }

    /// <summary>
    /// Where trade ingestion resumes for this holding. Opaque here: only the adapter that produced
    /// it (<c>ICryptoExchangeAdapter.GetTradesAsync</c>) interprets it. Null means "never walked".
    /// </summary>
    public string? TradeCursor { get; private set; }

    public int TradeCount { get; private set; }

    /// <summary>
    /// Set when the position left the venue (#435 S2). A closed row keeps its cursor and realized
    /// totals so a later reopen continues the same walk; current-holdings reads exclude it.
    /// </summary>
    public DateTime? ClosedAt { get; private set; }

    public bool IsClosed => ClosedAt is not null;

    // The forward ledger (#472) for venues whose fill history starts at connect — null until it
    // first runs, and always null for a venue with full history (Binance). Kept apart from the
    // displayed figures above: those go null while any lot is unpriced, the ledger never does.

    /// <summary>Quantity bought on the venue since connect, still held, with a known USD cost.</summary>
    public decimal? TrackedQuantity { get; private set; }

    /// <summary>The USD cost of <see cref="TrackedQuantity"/>.</summary>
    public decimal? TrackedCostUsd { get; private set; }

    /// <summary>
    /// Quantity whose cost the venue never showed: held before connect, or transferred in. While
    /// it is above zero the weighted-average cost is unknown, so cost basis stays null.
    /// </summary>
    public decimal? UntrackedQuantity { get; private set; }

    private CryptoHolding() { }

    public static CryptoHolding Create(
        Guid userId,
        string provider,
        string asset,
        decimal freeQuantity,
        decimal lockedQuantity,
        decimal usdValue,
        bool isFiat = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        return new CryptoHolding
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = provider,
            Asset = asset,
            FreeQuantity = freeQuantity,
            LockedQuantity = lockedQuantity,
            UsdValue = usdValue,
            IsFiat = isFiat,
            SyncedAt = DateTime.UtcNow,
        };
    }

    public void Update(decimal freeQuantity, decimal lockedQuantity, decimal usdValue, bool isFiat = false)
    {
        FreeQuantity = freeQuantity;
        LockedQuantity = lockedQuantity;
        UsdValue = usdValue;
        IsFiat = isFiat;
        SyncedAt = DateTime.UtcNow;
        ClosedAt = null;
    }

    public void Close()
    {
        FreeQuantity = 0m;
        LockedQuantity = 0m;
        UsdValue = 0m;
        SyncedAt = DateTime.UtcNow;
        ClosedAt ??= DateTime.UtcNow;
    }

    public void SetCostBasis(
        decimal? costBasisUsd,
        decimal? averageBuyPriceUsd,
        decimal? realizedPnlUsd,
        DateTime? lastTradeAt,
        int tradeCount)
    {
        CostBasisUsd = costBasisUsd;
        AverageBuyPriceUsd = averageBuyPriceUsd;
        RealizedPnlUsd = realizedPnlUsd;
        LastTradeAt = lastTradeAt;
        TradeCount = tradeCount;
    }

    public void SetForwardLedger(decimal trackedQuantity, decimal trackedCostUsd, decimal untrackedQuantity)
    {
        TrackedQuantity = trackedQuantity;
        TrackedCostUsd = trackedCostUsd;
        UntrackedQuantity = untrackedQuantity;
    }

    public void AdvanceTradeCursor(string? cursor)
    {
        TradeCursor = cursor;
    }
}
