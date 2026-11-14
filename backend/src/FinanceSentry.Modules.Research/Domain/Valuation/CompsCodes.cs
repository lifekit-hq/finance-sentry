namespace FinanceSentry.Modules.Research.Domain.Valuation;

/// <summary>Typed flags on a <c>get_comps_valuation</c> result: the caveats a client should word, not a log.</summary>
public static class CompsFlags
{
    public const string FinancialSector = "FINANCIAL_SECTOR";
    public const string PeerIndustryMismatch = "PEER_INDUSTRY_MISMATCH";
    public const string PeersDropped = "PEERS_DROPPED";
    public const string PeOutlierExcluded = "PE_OUTLIER_EXCLUDED";
    public const string TargetPeOutlier = "TARGET_PE_OUTLIER";
    public const string MultipleNotEvaluable = "MULTIPLE_NOT_EVALUABLE";
    public const string EvRevenueFallback = "EV_REVENUE_FALLBACK";
    public const string NetDebtUnavailable = "NET_DEBT_UNAVAILABLE";
    public const string SharesFromMarketCap = "SHARES_FROM_MARKET_CAP";
    public const string FxConverted = "FX_CONVERTED";
    public const string FxUnknown = "FX_UNKNOWN";
}

/// <summary>Why a peer was dropped, a peer left out of one multiple, or a multiple could not be evaluated.</summary>
public static class CompsReasons
{
    public const string FetchFailed = "FETCH_FAILED";
    public const string NotEquity = "NOT_EQUITY";
    public const string MissingData = "MISSING_DATA";
    public const string NegativeEbitda = "NEGATIVE_EBITDA";
    public const string NegativeEarnings = "NEGATIVE_EARNINGS";
    public const string NegativeBookValue = "NEGATIVE_BOOK_VALUE";
    public const string PeOutlier = "PE_OUTLIER";
    public const string InsufficientPeers = "INSUFFICIENT_PEERS";
    public const string FinancialSector = "FINANCIAL_SECTOR";
    public const string DataUnavailable = "DATA_UNAVAILABLE";
    public const string TargetPrefix = "TARGET_";
}
