namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Reads the valuation axis of one symbol's scorecard and, from the same Yahoo read, its sector and industry (null when
/// unknown); plus the peers' forward P/Es, read only when the own-history premium is missing and the name has a forward
/// P/E. It builds no peer set and persists nothing, so a scorecard read stays a read. Shared by the scorecard
/// query and the ETF look-through, which scores each top holding the way the query scores a single name.
/// </summary>
public sealed class ScorecardValuationReader(
    IValuationDataService valuation, IValuationHistoryService history, PeerForwardPeReader peers)
{
    public async Task<(ScorecardValuationAxis Axis, string? Sector, string? Industry, IReadOnlyList<decimal?>? PeerForwardPes)> ReadAsync(
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
