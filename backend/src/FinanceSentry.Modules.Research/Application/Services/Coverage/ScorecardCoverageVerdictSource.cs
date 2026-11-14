namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="ICoverageVerdictSource"/> over the scorecard read (<see cref="GetScorecardQuery"/>), so a coverage
/// verdict can never disagree with the dossier or the construction screen. No model call.
/// </summary>
public sealed class ScorecardCoverageVerdictSource(
    IQueryHandler<GetScorecardQuery, ScorecardResult> scorecard,
    ILogger<ScorecardCoverageVerdictSource> logger) : ICoverageVerdictSource
{
    public async Task<CoverageVerdictReading?> ReadAsync(string ticker, CancellationToken ct = default)
    {
        try
        {
            var card = await scorecard.Handle(new GetScorecardQuery(ticker), ct);
            return new CoverageVerdictReading(card.Verdict.Word, card.Verdict.FiredRuleId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Coverage: the scorecard for {Ticker} could not be read; no verdict recorded today", ticker);
            return null;
        }
    }
}
