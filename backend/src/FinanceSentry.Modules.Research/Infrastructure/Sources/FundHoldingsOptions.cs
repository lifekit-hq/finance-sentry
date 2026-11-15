namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

/// <summary>
/// Where an ETF's holdings are read when Yahoo lists none (section <c>FundHoldings</c>, issue #1324). Each entry maps a
/// fund ticker to the issuer's published holdings file; nothing ships by default, so an unmapped fund simply has no
/// issuer fallback. Only <c>https</c> URLs are fetched.
/// </summary>
public sealed class FundHoldingsOptions
{
    public const string SectionName = "FundHoldings";

    /// <summary>Fund ticker (any case) to the URL of the issuer's holdings CSV.</summary>
    public Dictionary<string, string> IssuerFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
