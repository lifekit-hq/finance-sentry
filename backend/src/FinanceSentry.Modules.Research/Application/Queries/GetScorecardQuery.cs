namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;

/// <summary>
/// One symbol's scorecard with its verdict word, the facts behind each axis and the rule that fired
/// (spec 726 US2, FR-021). The valuation axis reads current metrics and the trailing-P/E history directly:
/// unlike <see cref="GetValuationSnapshotQuery"/> it builds no peer set and persists no snapshot row, so a
/// scorecard read stays a read.
/// </summary>
public sealed record GetScorecardQuery(string Symbol) : IQuery<ScorecardResult>;

public sealed class GetScorecardQueryHandler(
    IValuationDataService valuation,
    IValuationHistoryService history,
    ScorecardReader reader)
    : IQueryHandler<GetScorecardQuery, ScorecardResult>
{
    public async Task<ScorecardResult> Handle(GetScorecardQuery query, CancellationToken ct)
    {
        var symbol = query.Symbol.Trim().ToUpperInvariant();
        var axis = await ReadValuationAsync(symbol, ct);
        return await reader.ReadAsync(symbol, axis, null, ct);
    }

    private async Task<ScorecardValuationAxis> ReadValuationAsync(string symbol, CancellationToken ct)
    {
        var current = await valuation.GetCurrentMetricsAsync(symbol, ct);
        if (current is null)
        {
            return ScorecardValuationAxis.Unavailable;
        }

        if (current.NotApplicable)
        {
            return ScorecardValuationAxis.NotApplicableAxis;
        }

        var trailing = await history.GetTrailingPeHistoryAsync(symbol, ct);
        var (currency, divisor) = MinorUnitCurrency.Normalize(current.Currency);
        return ScorecardValuationAxis.From(
            current.TrailingPe,
            trailing.FiveYearAvg,
            trailing.WindowYears,
            current.ForwardPe,
            current.Price / divisor,
            current.ConsensusTarget / divisor,
            currency,
            current.IsStale);
    }
}
