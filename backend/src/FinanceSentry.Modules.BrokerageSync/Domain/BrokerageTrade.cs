namespace FinanceSentry.Modules.BrokerageSync.Domain;

/// <summary>
/// One IBKR trade execution pulled from the Flex Web Service Activity Flex Query
/// (fs-435 S5). Keyed per user on IBKR's own execution id so re-pulling an
/// overlapping window — the normal case, since Flex windows cap at 365 days — is a
/// no-op rather than a duplicate row.
///
/// Every money field is stored in the trade's own <see cref="Currency"/>, never
/// converted at ingestion. A cross-account aggregate over these rows must convert
/// at the reader boundary via <c>CurrencyConverter.ToUsd</c> using <see cref="Currency"/>
/// / <see cref="FxRateToBase"/> — never sum the native amounts directly.
/// </summary>
public sealed class BrokerageTrade
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Provider { get; private set; } = string.Empty;

    /// <summary>IBKR's own execution id (Flex <c>ibExecID</c>) — the idempotency key.</summary>
    public string IbExecutionId { get; private set; } = string.Empty;

    /// <summary>IBKR's trade id (Flex <c>tradeID</c>) — can repeat across allocations/partial fills, not unique.</summary>
    public string IbTradeId { get; private set; } = string.Empty;

    public long? Conid { get; private set; }
    public string? Isin { get; private set; }
    public string Symbol { get; private set; } = string.Empty;

    /// <summary>Links to the durable <see cref="BrokerageInstrument"/> row when the trade carries a conid.</summary>
    public Guid? InstrumentId { get; private set; }

    /// <summary>"O" (open) or "C" (close), per Flex's Open/Close Indicator.</summary>
    public string? OpenCloseIndicator { get; private set; }

    /// <summary>"the date and time of the initial trade" (Flex <c>openDateTime</c>) — the acquisition date for a closing trade.</summary>
    public DateTime? OpenDateTime { get; private set; }

    public DateTime TradeDateTime { get; private set; }

    public decimal Quantity { get; private set; }
    public decimal Price { get; private set; }
    public decimal Proceeds { get; private set; }
    public decimal? CostBasis { get; private set; }
    public decimal? RealizedPnl { get; private set; }

    public decimal? Commission { get; private set; }
    public string? CommissionCurrency { get; private set; }
    public decimal? Taxes { get; private set; }

    public string Currency { get; private set; } = string.Empty;
    public decimal? FxRateToBase { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private BrokerageTrade() { }

    public BrokerageTrade(
        Guid userId,
        string provider,
        string ibExecutionId,
        string ibTradeId,
        long? conid,
        string? isin,
        string symbol,
        string? openCloseIndicator,
        DateTime? openDateTime,
        DateTime tradeDateTime,
        decimal quantity,
        decimal price,
        decimal proceeds,
        decimal? costBasis,
        decimal? realizedPnl,
        decimal? commission,
        string? commissionCurrency,
        decimal? taxes,
        string currency,
        decimal? fxRateToBase,
        Guid? instrumentId = null)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Provider = provider;
        IbExecutionId = ibExecutionId;
        IbTradeId = ibTradeId;
        Conid = conid;
        Isin = isin;
        Symbol = symbol;
        InstrumentId = instrumentId;
        OpenCloseIndicator = openCloseIndicator;
        OpenDateTime = openDateTime;
        TradeDateTime = tradeDateTime;
        Quantity = quantity;
        Price = price;
        Proceeds = proceeds;
        CostBasis = costBasis;
        RealizedPnl = realizedPnl;
        Commission = commission;
        CommissionCurrency = commissionCurrency;
        Taxes = taxes;
        Currency = currency;
        FxRateToBase = fxRateToBase;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>
    /// Refreshes every field from a re-pull of the same execution (e.g. IBKR corrects a
    /// commission or an FX rate after the fact). Never changes <see cref="IbExecutionId"/>
    /// or <see cref="UserId"/> — those are the identity of the row.
    /// </summary>
    public void Refresh(
        long? conid,
        string? isin,
        string symbol,
        string? openCloseIndicator,
        DateTime? openDateTime,
        DateTime tradeDateTime,
        decimal quantity,
        decimal price,
        decimal proceeds,
        decimal? costBasis,
        decimal? realizedPnl,
        decimal? commission,
        string? commissionCurrency,
        decimal? taxes,
        string currency,
        decimal? fxRateToBase,
        Guid? instrumentId)
    {
        Conid = conid;
        Isin = isin;
        Symbol = symbol;
        InstrumentId = instrumentId ?? InstrumentId;
        OpenCloseIndicator = openCloseIndicator;
        OpenDateTime = openDateTime;
        TradeDateTime = tradeDateTime;
        Quantity = quantity;
        Price = price;
        Proceeds = proceeds;
        CostBasis = costBasis;
        RealizedPnl = realizedPnl;
        Commission = commission;
        CommissionCurrency = commissionCurrency;
        Taxes = taxes;
        Currency = currency;
        FxRateToBase = fxRateToBase;
        UpdatedAt = DateTime.UtcNow;
    }
}
