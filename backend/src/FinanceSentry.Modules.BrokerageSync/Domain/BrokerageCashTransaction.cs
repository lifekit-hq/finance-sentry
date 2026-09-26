namespace FinanceSentry.Modules.BrokerageSync.Domain;

/// <summary>
/// One IBKR cash transaction row pulled from the Flex Web Service Activity Flex Query
/// (fs-435 S5) — deposits/withdrawals, dividends, withholding, and similar cash events.
/// No dividend-specific logic lives here: this is a storage-only landing table, and
/// interpreting these rows (e.g. matching dividends to their withholding) is S8's job.
///
/// IBKR does not hand back a stable row id for cash transactions, so idempotency is
/// keyed on a deterministic composite of the row's own reported fields
/// (<see cref="IdempotencyKey"/>) rather than a broker-issued identifier.
///
/// Money is stored in the transaction's own <see cref="Currency"/>, never converted at
/// ingestion — see <see cref="BrokerageTrade"/>'s doc comment for the same rule.
/// </summary>
public sealed class BrokerageCashTransaction
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Provider { get; private set; } = string.Empty;

    /// <summary>Deterministic hash of the row's stable reported fields — the idempotency key.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    public long? Conid { get; private set; }
    public string? Isin { get; private set; }
    public string? Symbol { get; private set; }

    public DateTime DateTime { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>Flex's own transaction type string (e.g. "Dividends", "Deposits/Withdrawals").</summary>
    public string TransactionType { get; private set; } = string.Empty;

    public string? Code { get; private set; }

    /// <summary>The "871(m) Withholding" field, when present.</summary>
    public decimal? WithholdingTax { get; private set; }

    public string Currency { get; private set; } = string.Empty;
    public decimal? FxRateToBase { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private BrokerageCashTransaction() { }

    public BrokerageCashTransaction(
        Guid userId,
        string provider,
        string idempotencyKey,
        long? conid,
        string? isin,
        string? symbol,
        DateTime dateTime,
        decimal amount,
        string transactionType,
        string? code,
        decimal? withholdingTax,
        string currency,
        decimal? fxRateToBase)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Provider = provider;
        IdempotencyKey = idempotencyKey;
        Conid = conid;
        Isin = isin;
        Symbol = symbol;
        DateTime = dateTime;
        Amount = amount;
        TransactionType = transactionType;
        Code = code;
        WithholdingTax = withholdingTax;
        Currency = currency;
        FxRateToBase = fxRateToBase;
        CreatedAt = DateTime.UtcNow;
    }
}
