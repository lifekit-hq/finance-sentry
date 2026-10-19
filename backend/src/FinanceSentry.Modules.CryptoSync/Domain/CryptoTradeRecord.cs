using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;

namespace FinanceSentry.Modules.CryptoSync.Domain;

/// <summary>
/// One persisted fill from a venue's walk (#435 S1). Every field a CGT schedule needs, kept for as
/// long as the disposal it represents matters for tax purposes — unlike <see cref="CryptoHolding"/>,
/// this row survives a position closing or the holding row disappearing. Idempotent on
/// <c>(UserId, Provider, TradeId)</c>: a re-walk of an already-covered page adds nothing.
/// </summary>
public sealed class CryptoTradeRecord : IUserOwned
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string TradeId { get; private set; } = string.Empty;
    public string Asset { get; private set; } = string.Empty;
    public string QuoteAsset { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal PriceUsd { get; private set; }
    public decimal QuoteQuantityUsd { get; private set; }
    public bool IsBuyer { get; private set; }

    /// <summary>The commission the venue charged, in <see cref="FeeAsset"/>. Null on rows synced before fees were kept (never backfilled).</summary>
    public decimal? Fee { get; private set; }
    public string? FeeAsset { get; private set; }

    /// <summary><see cref="Fee"/> in USD at the fill's own time; null when unvalued.</summary>
    public decimal? FeeUsd { get; private set; }
    public DateTime Timestamp { get; private set; }
    public DateTime RecordedAt { get; private set; }

    private CryptoTradeRecord() { }

    public static CryptoTradeRecord Create(Guid userId, string provider, CryptoTrade trade)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(trade);

        return new CryptoTradeRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = provider,
            TradeId = trade.TradeId,
            Asset = trade.Asset,
            QuoteAsset = trade.QuoteAsset,
            Quantity = trade.Quantity,
            PriceUsd = trade.PriceUsd,
            QuoteQuantityUsd = trade.QuoteQuantityUsd,
            IsBuyer = trade.IsBuyer,
            Fee = trade.Fee,
            FeeAsset = trade.FeeAsset,
            FeeUsd = trade.FeeUsd,
            Timestamp = trade.Timestamp,
            RecordedAt = DateTime.UtcNow,
        };
    }
}
