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

    private CryptoHolding() { }

    public static CryptoHolding Create(
        Guid userId,
        string provider,
        string asset,
        decimal freeQuantity,
        decimal lockedQuantity,
        decimal usdValue)
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
            SyncedAt = DateTime.UtcNow,
        };
    }

    public void Update(decimal freeQuantity, decimal lockedQuantity, decimal usdValue)
    {
        FreeQuantity = freeQuantity;
        LockedQuantity = lockedQuantity;
        UsdValue = usdValue;
        SyncedAt = DateTime.UtcNow;
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

    public void AdvanceTradeCursor(string? cursor)
    {
        TradeCursor = cursor;
    }
}
