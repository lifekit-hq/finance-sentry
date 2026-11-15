namespace FinanceSentry.Modules.Research.Application.Services;

/// <summary>
/// Current valuation metrics for a ticker from a free public source (Yahoo <c>quoteSummary</c>). The
/// query handler composes this with self-built history and a peer set. Non-equity tickers come back
/// with <see cref="ValuationCurrentMetrics.NotApplicable"/> set — never fabricated fundamentals
/// (feature 030, FR-006).
/// </summary>
public interface IValuationDataService
{
    /// <summary>Current metrics for one ticker. Returns <c>null</c> only on a hard fetch failure.</summary>
    Task<ValuationCurrentMetrics?> GetCurrentMetricsAsync(string ticker, CancellationToken ct = default);

    /// <summary>Default peer symbols for a ticker (Yahoo "similar" recommendations). Empty on failure.</summary>
    Task<IReadOnlyList<string>> GetPeerSymbolsAsync(string ticker, CancellationToken ct = default);
}

/// <summary>
/// Point-in-time current valuation metrics for one ticker. Every ratio is nullable: a missing metric
/// is <c>null</c>, never zero (FR-006). <see cref="NotApplicable"/> = non-equity quote type.
/// <see cref="Name"/> is the company name from the already-fetched Yahoo <c>price</c> module;
/// <see cref="PriceAsOf"/> is when the shared quote path last saw the price (null when only the
/// quoteSummary price is known). <see cref="TargetLow"/> / <see cref="TargetHigh"/> bound the analysts'
/// price targets around <see cref="ConsensusTarget"/> (their mean), from <see cref="AnalystCount"/>
/// opinions; <see cref="Currency"/> is the quote currency every price is in. <see cref="Beta"/> is Yahoo's
/// five-year monthly beta against the market (the DCF's cost-of-equity input); null when Yahoo has none.
/// </summary>
public sealed record ValuationCurrentMetrics(
    string Ticker,
    decimal? Price,
    decimal? TrailingPe,
    decimal? ForwardPe,
    decimal? EvToEbitda,
    decimal? DividendYield,
    decimal? ConsensusTarget,
    bool IsStale,
    bool NotApplicable,
    string? Sector,
    string? Industry,
    string? Name = null,
    DateTimeOffset? PriceAsOf = null,
    decimal? TargetLow = null,
    decimal? TargetHigh = null,
    int? AnalystCount = null,
    string? Currency = null,
    decimal? MarketCap = null,
    ValuationCompsInputs? Comps = null,
    decimal? Beta = null);

/// <summary>
/// The raw trailing-twelve-month figures from the same Yahoo <c>quoteSummary</c> call that the comps
/// tool compares across a peer set. Amounts (<see cref="EnterpriseValue"/>, <see cref="Ebitda"/>,
/// <see cref="TotalRevenue"/>, <see cref="TotalDebt"/>, <see cref="TotalCash"/>) are in
/// <see cref="FinancialCurrency"/> (the reporting currency); <see cref="TrailingEps"/> and
/// <see cref="BookValuePerShare"/> are per share. Every field is nullable: a missing field is
/// <c>null</c>, never zero.
/// </summary>
public sealed record ValuationCompsInputs(
    decimal? EnterpriseValue,
    decimal? Ebitda,
    decimal? TotalRevenue,
    decimal? RevenueGrowth,
    decimal? EbitdaMargin,
    decimal? PriceToBook,
    decimal? TrailingEps,
    decimal? BookValuePerShare,
    decimal? TotalDebt,
    decimal? TotalCash,
    decimal? SharesOutstanding,
    string? FinancialCurrency);
