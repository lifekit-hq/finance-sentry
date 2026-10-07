namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Modules.Research.Domain.Fundamentals;

/// <summary>
/// <c>get_fundamentals</c> / <c>GET research/fundamentals/{ticker}</c> (#837): the facts plus an
/// explicit coverage answer, so "no fundamentals" always says why instead of being a bare empty list.
/// </summary>
public record FundamentalsDto(
    string Ticker,
    FundamentalsCoverage Coverage,
    IReadOnlyList<FundamentalFactDto> Facts);
