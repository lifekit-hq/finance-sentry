namespace FinanceSentry.Modules.Research.Domain.Valuation;

/// <summary>Typed flags on a <c>get_dcf_valuation</c> result: the caveats a client should word, not a log.</summary>
public static class DcfFlags
{
    public const string TerminalHeavy = "TERMINAL_HEAVY";
    public const string GrowthCapped = "GROWTH_CAPPED";
    public const string TaxRateDefaulted = "TAX_RATE_DEFAULTED";
    public const string CostOfDebtDefaulted = "COST_OF_DEBT_DEFAULTED";
    public const string NwcAssumedZero = "NWC_ASSUMED_ZERO";
    public const string TerminalGrowthCapped = "TERMINAL_GROWTH_CAPPED";
    public const string WaccFloored = "WACC_FLOORED";
    public const string BaseYearTtm = "BASE_YEAR_TTM";
    public const string ImpliedGrowthOutOfRange = "IMPLIED_GROWTH_OUT_OF_RANGE";
    public const string ExitMultipleUnavailable = "EXIT_MULTIPLE_UNAVAILABLE";
    public const string OverridesApplied = "OVERRIDES_APPLIED";

    // The equity bridge is the comps tool's, so its caveats carry the same codes.
    public const string FxConverted = CompsFlags.FxConverted;
    public const string FxUnknown = CompsFlags.FxUnknown;
    public const string SharesFromMarketCap = CompsFlags.SharesFromMarketCap;
}

/// <summary>Why a DCF could not be evaluated: the typed reason that replaces a number, never a fabricated one.</summary>
public static class DcfReasons
{
    public const string DataUnavailable = "DATA_UNAVAILABLE";
    public const string FinancialSector = "FINANCIAL_SECTOR";
    public const string NoOperatingProfit = "NO_OPERATING_PROFIT";
    public const string InsufficientHistory = "INSUFFICIENT_HISTORY";
    public const string CashFlowUnavailable = "CASH_FLOW_UNAVAILABLE";
    public const string PriceUnavailable = "PRICE_UNAVAILABLE";
    public const string BetaUnavailable = "BETA_UNAVAILABLE";
    public const string RiskFreeUnavailable = "RISK_FREE_UNAVAILABLE";
    public const string SharesUnavailable = "SHARES_UNAVAILABLE";
    public const string EquityBridgeUnavailable = "EQUITY_BRIDGE_UNAVAILABLE";
    public const string NegativeEquityValue = "NEGATIVE_EQUITY_VALUE";
    public const string TerminalGrowthNotBelowWacc = "TERMINAL_GROWTH_NOT_BELOW_WACC";
    public const string InvalidOverride = "INVALID_OVERRIDE";
}
