namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;

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
    private readonly ScorecardValuationReader valuationReader = new(valuation, history, peers);

    public async Task<ScorecardResult> Handle(GetScorecardQuery query, CancellationToken ct)
    {
        var symbol = query.Symbol.Trim().ToUpperInvariant();
        var (axis, sector, industry, peerForwardPes) = await valuationReader.ReadAsync(symbol, ct);
        return await reader.ReadAsync(symbol, axis, null, ct, sector, industry, peerForwardPes);
    }
}
