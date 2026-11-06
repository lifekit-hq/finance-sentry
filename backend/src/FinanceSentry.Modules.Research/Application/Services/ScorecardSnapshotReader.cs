namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary><see cref="IScorecardSnapshotReader"/> impl over Research's own <see cref="GetScorecardQuery"/>.</summary>
public sealed class ScorecardSnapshotReader(IQueryHandler<GetScorecardQuery, ScorecardResult> scorecard)
    : IScorecardSnapshotReader
{
    public async Task<ScorecardSnapshot> GetAsync(string symbol, CancellationToken ct = default)
        => ToSnapshot(await scorecard.Handle(new GetScorecardQuery(symbol), ct));

    public static ScorecardSnapshot ToSnapshot(ScorecardResult card)
    {
        var fired = card.Verdict.Rules.FirstOrDefault(r => r.Fired);
        return new ScorecardSnapshot(
            card.Symbol,
            card.GeneratedAt,
            card.Structure.RawScore,
            card.Structure.AdjustedScore,
            card.Verdict.StructureBasis,
            card.Structure.NotEvaluableReasons,
            card.Fundamentals.Score,
            card.Fundamentals.NotEvaluableReasons,
            card.Crowding.Class,
            card.Crowding.ExtensionFromMa50,
            card.Valuation.NotApplicable,
            card.Valuation.TrailingPe,
            card.Valuation.TrailingPeFiveYearAvg,
            card.Valuation.PremiumPct,
            card.Valuation.ImpliedUpsidePct,
            card.Valuation.NotEvaluableReason,
            card.Valuation.IsStale,
            card.Verdict.Word,
            card.Verdict.RulesVersion,
            card.Verdict.FiredRuleId,
            fired?.Description,
            card.Sector,
            card.Industry);
    }
}
