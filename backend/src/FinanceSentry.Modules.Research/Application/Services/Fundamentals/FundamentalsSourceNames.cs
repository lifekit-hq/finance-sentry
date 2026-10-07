namespace FinanceSentry.Modules.Research.Application.Services.Fundamentals;

/// <summary>The provider names the shipped sources answer to — in provenance and in <c>Fundamentals:SourceOrder</c>.</summary>
public static class FundamentalsSourceNames
{
    /// <summary>SEC EDGAR XBRL company facts, us-gaap taxonomy (10-K / 10-Q, and us-gaap 20-F filers).</summary>
    public const string SecEdgarUsGaap = "sec-edgar-us-gaap";

    /// <summary>SEC EDGAR XBRL company facts, ifrs-full taxonomy (IFRS foreign private issuers' 20-F / 40-F).</summary>
    public const string SecEdgarIfrs = "sec-edgar-ifrs-full";

    /// <summary>Yahoo Finance fundamentals timeseries (key-less; the provider behind the valuation snapshot).</summary>
    public const string YahooFinance = "yahoo-finance";
}
