using System.Text.Json.Serialization;

namespace FinanceSentry.Modules.BrokerageSync.Domain;

/// <summary>
/// Tax classification of a broker instrument, in the Irish-tax sense used by the
/// deferred deemed-disposal engine (fs-435 S7). Set only by a human — never
/// inferred from symbol, description, <see cref="BrokerageInstrument.InstrumentType"/>
/// or <c>AssetClassNormalizer</c>. Null means unclassified, and unclassified
/// instruments must stay that way so a later tax schedule can block on them
/// rather than silently assume a regime.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<InstrumentClassification>))]
public enum InstrumentClassification
{
    /// <summary>An ordinary share, taxed under standard Irish CGT rules.</summary>
    OrdinaryShare = 1,

    /// <summary>
    /// An offshore fund / ETF within the gross roll-up regime — subject to the
    /// 8-year deemed-disposal exit-tax rules rather than CGT.
    /// </summary>
    OffshoreFund = 2,
}

/// <summary>
/// A durable broker instrument identity — one row per user per broker instrument,
/// keyed on the broker's own contract id (IBKR's <c>conid</c>). Survives a full
/// exit: unlike <see cref="BrokerageHolding"/>, which is reconciled away when a
/// position sells out, this row is never deleted, because the tax classification
/// a human attaches to it must outlive the position.
/// </summary>
public sealed class BrokerageInstrument
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public long Conid { get; private set; }
    public string? Isin { get; private set; }
    public string Symbol { get; private set; } = string.Empty;
    public string InstrumentType { get; private set; } = string.Empty;

    /// <summary>Null until a human sets it via the classification endpoint. Never written by sync.</summary>
    public InstrumentClassification? Classification { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private BrokerageInstrument() { }

    public BrokerageInstrument(
        Guid userId,
        string provider,
        long conid,
        string symbol,
        string instrumentType,
        string? isin = null)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Provider = provider;
        Conid = conid;
        Symbol = symbol;
        InstrumentType = instrumentType;
        Isin = isin;
        Classification = null;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>
    /// Refreshes symbol identity from the latest sync. Never touches
    /// <see cref="Classification"/> — that field is human-set only, and a sync
    /// must never overwrite it, including back to null.
    /// </summary>
    public void RefreshIdentity(string symbol, string instrumentType, string? isin)
    {
        Symbol = symbol;
        InstrumentType = instrumentType;
        // Only ever fills a previously-unknown ISIN; a feed that stops reporting
        // one (or never had one) must not blank out an ISIN captured earlier.
        if (!string.IsNullOrWhiteSpace(isin))
            Isin = isin;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>The one and only human-set path. Null clears the classification.</summary>
    public void SetClassification(InstrumentClassification? classification)
    {
        Classification = classification;
        UpdatedAt = DateTime.UtcNow;
    }
}
