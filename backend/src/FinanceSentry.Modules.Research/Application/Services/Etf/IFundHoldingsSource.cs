namespace FinanceSentry.Modules.Research.Application.Services.Etf;

/// <summary>What a symbol turned out to be when its fund holdings were asked for.</summary>
public enum FundKind
{
    /// <summary>The source answered and the symbol is not an ETF (an equity, crypto, an index, ...).</summary>
    NotAFund,

    /// <summary>An ETF. <see cref="FundHoldingsLookup.Holdings"/> may still be empty when no source listed them.</summary>
    Fund,

    /// <summary>The source could not say (it failed); the caller treats the symbol as it did before look-through.</summary>
    Unknown,
}

/// <summary>One line of a fund's holdings. <see cref="WeightPct"/> is percent of the fund (7.7 = 7.7%).</summary>
public sealed record FundHolding(string? Symbol, string? Name, decimal WeightPct);

/// <summary>
/// A fund's largest holdings with where they came from. <see cref="Source"/> is <see cref="FundHoldingsSources.Yahoo"/>
/// or <see cref="FundHoldingsSources.Issuer"/>; null when no holdings were found. <see cref="AsOf"/> is when the list
/// was read (the source's own as-of date is not given by Yahoo).
/// </summary>
public sealed record FundHoldingsLookup(
    FundKind Kind,
    IReadOnlyList<FundHolding> Holdings,
    string? Source,
    DateTimeOffset? AsOf)
{
    /// <summary>How many holdings the look-through reads: the fund's ten largest.</summary>
    public const int TopCount = 10;

    public static FundHoldingsLookup NotAFund { get; } = new(FundKind.NotAFund, [], null, null);

    public static FundHoldingsLookup Unknown { get; } = new(FundKind.Unknown, [], null, null);
}

/// <summary>The names a holdings list can come from.</summary>
public static class FundHoldingsSources
{
    public const string Yahoo = "yahoo";
    public const string Issuer = "issuer";
}

/// <summary>
/// The top holdings of an ETF (issue #1324): Yahoo <c>topHoldings</c> first, the issuer's published holdings file
/// when Yahoo lists none, cached for a day. Never throws for a source failure: it answers
/// <see cref="FundKind.Unknown"/> (could not tell) or a <see cref="FundKind.Fund"/> with no holdings.
/// </summary>
public interface IFundHoldingsSource
{
    Task<FundHoldingsLookup> GetTopHoldingsAsync(string symbol, CancellationToken ct = default);
}

/// <summary>
/// One provider in the chain behind <see cref="IFundHoldingsSource"/>. A provider that knows nothing about a symbol
/// answers a <see cref="FundKind.Fund"/> lookup with no holdings (or <see cref="FundKind.Unknown"/> on failure).
/// </summary>
public interface IFundHoldingsProvider
{
    Task<FundHoldingsLookup> LookupAsync(string symbol, CancellationToken ct);
}
