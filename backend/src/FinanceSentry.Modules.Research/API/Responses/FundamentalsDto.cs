namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Modules.Research.Domain.Fundamentals;

/// <summary>
/// <c>get_fundamentals</c> / <c>GET research/fundamentals/{ticker}</c> (#837): the facts plus an
/// explicit coverage answer, so "no fundamentals" always says why instead of being a bare empty list.
/// <see cref="FreeCashFlow"/> (last quarters + years, derived from the cash-flow facts) and
/// <see cref="ReturnOnEquity"/> (the ROIC proxy) each carry a reason when empty or null (#1376).
/// </summary>
public record FundamentalsDto(
    string Ticker,
    FundamentalsCoverage Coverage,
    IReadOnlyList<FundamentalFactDto> Facts,
    IReadOnlyList<FreeCashFlowPeriod> FreeCashFlow,
    string? FreeCashFlowReason,
    IReadOnlyList<ReturnOnEquityPeriod> ReturnOnEquity,
    string? ReturnOnEquityReason);
