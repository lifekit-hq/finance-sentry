namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;

/// <summary>
/// One symbol's scorecard with its verdict word, the facts behind each axis and the rule that fired
/// (spec 726 US2, FR-021). The valuation axis reads current metrics and the trailing-P/E history directly:
/// unlike <see cref="GetValuationSnapshotQuery"/> it builds no peer set and persists no snapshot row, so a
/// scorecard read stays a read. The one peer read it makes is the verdict's fallback price lens, asked only when
/// the own five-year P/E history is missing (rules v2).
/// </summary>
public sealed record GetScorecardQuery(string Symbol) : IQuery<ScorecardResult>;

public sealed class GetScorecardQueryHandler(
    IValuationDataService valuation,
    IValuationHistoryService history,
    PeerForwardPeReader peers,
    ScorecardReader reader)
    : IQueryHandler<GetScorecardQuery, ScorecardResult>
{
    public async Task<ScorecardResult> Handle(GetScorecardQuery query, CancellationToken ct)
    {
        var symbol = query.Symbol.Trim().ToUpperInvariant();
        var (axis, sector, industry, peerForwardPes) = await ReadValuationAsync(symbol, ct);
        return await reader.ReadAsync(symbol, axis, null, ct, sector, industry, peerForwardPes);
    }

    /// <summary>
    /// The valuation axis and, from the same Yahoo read, the company's sector and industry (null when unknown); plus
    /// the peers' forward P/Es, read only when the own-history premium is missing and the name has a forward P/E.
    /// </summary>
    private async Task<(ScorecardValuationAxis Axis, string? Sector, string? Industry, IReadOnlyList<decimal?>? PeerForwardPes)> ReadValuationAsync(
        string symbol, CancellationToken ct)
    {
        var current = await valuation.GetCurrentMetricsAsync(symbol, ct);
        if (current is null)
        {
            return (ScorecardValuationAxis.Unavailable, null, null, null);
        }

        if (current.NotApplicable)
        {
            return (ScorecardValuationAxis.NotApplicableAxis, null, null, null);
        }

        var trailing = await history.GetTrailingPeHistoryAsync(symbol, ct);
        var (currency, divisor) = MinorUnitCurrency.Normalize(current.Currency);
        var axis = ScorecardValuationAxis.From(
            current.TrailingPe,
            trailing.FiveYearAvg,
            trailing.WindowYears,
            current.ForwardPe,
            current.Price / divisor,
            current.ConsensusTarget / divisor,
            currency,
            current.IsStale);
        var peerForwardPes = axis.PremiumPct is null && current.ForwardPe is > 0m
            ? await peers.ReadAsync(symbol, ct)
            : null;
        return (axis, current.Sector, current.Industry, peerForwardPes);
    }
}
