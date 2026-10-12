namespace FinanceSentry.Modules.BrokerageSync.Domain;

using FinanceSentry.Core.Auth;

public sealed class BrokerageHolding : IUserOwned
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Symbol { get; private set; } = string.Empty;
    public string InstrumentType { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UsdValue { get; private set; }

    /// <summary>ISO code the position is denominated in (<c>USD</c> unless the provider reports otherwise).</summary>
    public string Currency { get; private set; } = DefaultCurrency;

    /// <summary>
    /// The position's value in <see cref="Currency"/>, exactly as the provider reported it (never converted).
    /// Null when the provider's rows are USD-only, so a reader falls back to <see cref="UsdValue"/>.
    /// </summary>
    public decimal? NativeValue { get; private set; }

    public DateTime SyncedAt { get; private set; }
    public string Provider { get; private set; } = string.Empty;

    public decimal? AverageCostUsd { get; private set; }
    public decimal? CostBasisUsd { get; private set; }
    public DateTime? AcquiredAt { get; private set; }

    /// <summary>Links to the durable <see cref="BrokerageInstrument"/> row, when the broker gave us an instrument id.</summary>
    public Guid? InstrumentId { get; private set; }

    /// <summary>
    /// Set only for holdings sourced from the daily Flex statement: the statement date the quantity and
    /// value are as of. Null for live (OAuth) holdings.
    /// </summary>
    public DateOnly? FlexAsOfDate { get; private set; }

    public const string DefaultCurrency = "USD";

    private BrokerageHolding() { }

    public BrokerageHolding(
        Guid userId,
        string symbol,
        string instrumentType,
        decimal quantity,
        decimal usdValue,
        string provider,
        decimal? averageCostUsd = null,
        DateTime? acquiredAt = null,
        Guid? instrumentId = null,
        DateOnly? flexAsOfDate = null,
        string currency = DefaultCurrency,
        decimal? nativeValue = null)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Symbol = symbol;
        InstrumentType = instrumentType;
        Quantity = quantity;
        UsdValue = usdValue;
        SyncedAt = DateTime.UtcNow;
        Provider = provider;
        AverageCostUsd = averageCostUsd;
        CostBasisUsd = averageCostUsd.HasValue ? Math.Round(averageCostUsd.Value * quantity, 4) : null;
        AcquiredAt = acquiredAt;
        InstrumentId = instrumentId;
        FlexAsOfDate = flexAsOfDate;
        Currency = currency;
        NativeValue = nativeValue;
    }

    public void Update(decimal quantity, decimal usdValue, Guid? instrumentId = null, DateOnly? flexAsOfDate = null)
    {
        FlexAsOfDate = flexAsOfDate;
        Quantity = quantity;
        UsdValue = usdValue;
        SyncedAt = DateTime.UtcNow;
        if (AverageCostUsd is decimal avg)
            CostBasisUsd = Math.Round(avg * quantity, 4);
        if (instrumentId.HasValue)
            InstrumentId = instrumentId;
    }

    public void SetCostBasis(decimal? averageCostUsd, DateTime? acquiredAt)
    {
        AverageCostUsd = averageCostUsd;
        CostBasisUsd = averageCostUsd.HasValue ? Math.Round(averageCostUsd.Value * Quantity, 4) : null;
        AcquiredAt = acquiredAt;
    }
}
