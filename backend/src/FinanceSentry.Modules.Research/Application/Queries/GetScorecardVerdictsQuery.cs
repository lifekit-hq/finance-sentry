namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using Microsoft.Extensions.Logging;

/// <summary>
/// The verdict word of many symbols in one read, for the Accounts position chips (spec 726 US2): one request
/// instead of one scorecard call per row. Each symbol goes through the same <see cref="GetScorecardQuery"/> as the
/// single read, so a chip can never disagree with the dossier. A symbol whose scorecard cannot be read is left out
/// (and logged), so one bad name never blanks the rest. Input is trimmed, upper-cased, de-duplicated and capped at
/// <see cref="MaxSymbols"/>.
/// </summary>
public sealed record GetScorecardVerdictsQuery(IReadOnlyList<string> Symbols) : IQuery<IReadOnlyList<ScorecardVerdictSummary>>
{
    public const int MaxSymbols = 50;
}

public sealed class GetScorecardVerdictsQueryHandler(
    IQueryHandler<GetScorecardQuery, ScorecardResult> scorecard,
    ILogger<GetScorecardVerdictsQueryHandler> logger)
    : IQueryHandler<GetScorecardVerdictsQuery, IReadOnlyList<ScorecardVerdictSummary>>
{
    public async Task<IReadOnlyList<ScorecardVerdictSummary>> Handle(GetScorecardVerdictsQuery query, CancellationToken ct)
    {
        var symbols = query.Symbols
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0)
            .Distinct()
            .Take(GetScorecardVerdictsQuery.MaxSymbols)
            .ToList();

        // One after another: the scorecard reads share this request's scoped DbContext, which allows a single
        // operation at a time.
        var result = new List<ScorecardVerdictSummary>(symbols.Count);
        foreach (var symbol in symbols)
        {
            try
            {
                var card = await scorecard.Handle(new GetScorecardQuery(symbol), ct);
                result.Add(new ScorecardVerdictSummary(symbol, card.Verdict.Word, card.Verdict.FiredRuleId));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Scorecard verdict for {Symbol} could not be read and was left out", symbol);
            }
        }

        return result;
    }
}
