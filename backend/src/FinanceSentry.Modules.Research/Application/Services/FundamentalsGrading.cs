namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain.Scoring;
using Microsoft.Extensions.Logging;

/// <summary>
/// EDGAR fundamentals grade per ticker, the way every scan stage needs it: a ticker EDGAR cannot
/// answer for — crypto, an ETF, a delisted filer, an upstream failure — grades null and keeps its
/// momentum-only standing, and one bad ticker never aborts the run (019 FR-013).
/// </summary>
internal static class FundamentalsGrading
{
    public static async Task<IReadOnlyDictionary<string, int?>> GradeAsync(
        ISecEdgarService secEdgar,
        ILogger logger,
        IEnumerable<string> tickers,
        CancellationToken ct)
    {
        var grades = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (var ticker in tickers)
        {
            if (grades.ContainsKey(ticker))
            {
                continue;
            }

            try
            {
                var facts = await secEdgar.GetFundamentalsAsync(ticker, FundamentalsScorer.FactsPerConcept, ct);
                grades[ticker] = FundamentalsScorer.Score(facts).Score;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                grades[ticker] = null;
                logger.LogWarning(ex,
                    "Opportunity scan could not grade fundamentals for {Ticker}; ranking it on momentum only",
                    ticker);
            }
        }

        return grades;
    }
}
