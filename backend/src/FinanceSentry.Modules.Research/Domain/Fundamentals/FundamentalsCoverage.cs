namespace FinanceSentry.Modules.Research.Domain.Fundamentals;

/// <summary>
/// What the fundamentals chain could supply for one ticker (#837) — the explicit answer that
/// replaces a bare empty list. <see cref="Status"/> is one of <see cref="FundamentalsCoverageStatus"/>;
/// <see cref="IssuerType"/> one of <see cref="FundamentalsIssuerType"/> (null when no source could
/// tell); <see cref="Reason"/> explains anything short of full coverage; <see cref="Bases"/> names the
/// provider behind each period basis that was supplied.
/// </summary>
public sealed record FundamentalsCoverage(
    string Status,
    string? IssuerType,
    string? Reason,
    IReadOnlyList<FundamentalsBasisCoverage> Bases);

/// <summary>
/// One supplied period basis (<see cref="FundamentalsBasis"/>): the provider whose series it is, the
/// newest period end in it, and whether that series is older than the chain's freshness window
/// (used only because no source had a fresh one).
/// </summary>
public sealed record FundamentalsBasisCoverage(
    string Basis,
    string Provider,
    DateOnly LatestPeriodEnd,
    bool Stale);

public static class FundamentalsCoverageStatus
{
    /// <summary>At least one period basis was supplied.</summary>
    public const string Covered = "covered";

    /// <summary>Every source answered and none has fundamentals for the ticker.</summary>
    public const string NoSourceAvailable = "no_source_available";

    /// <summary>The issuer is of a kind no company-fundamentals source covers (an investment fund).</summary>
    public const string UnsupportedIssuerType = "unsupported_issuer_type";

    /// <summary>A source failed (provider error, not "no data") before anything was supplied — retry later.</summary>
    public const string SourceUnavailable = "source_unavailable";
}

public static class FundamentalsIssuerType
{
    /// <summary>Files 10-K / 10-Q.</summary>
    public const string DomesticFiler = "domestic_filer";

    /// <summary>Files 20-F / 40-F annual reports and 6-K interim furnishings.</summary>
    public const string ForeignPrivateIssuer = "foreign_private_issuer";

    /// <summary>Files fund reports (N-CSR, N-PORT, 485BPOS, ...): an ETF or mutual fund, no company financials.</summary>
    public const string InvestmentFund = "investment_fund";

    /// <summary>Not in the SEC's ticker map at all.</summary>
    public const string NotSecRegistrant = "not_sec_registrant";
}

public static class FundamentalsBasis
{
    public const string Quarterly = "quarterly";
    public const string Annual = "annual";
}
