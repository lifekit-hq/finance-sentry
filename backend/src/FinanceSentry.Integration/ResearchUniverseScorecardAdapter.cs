namespace FinanceSentry.Integration;

using FinanceSentry.Modules.PortfolioConstruction.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;
using Microsoft.Extensions.Logging;

/// <summary>
/// Spec 726: implements the Portfolio Construction module's <see cref="IUniverseScorecardSource"/> over Research's
/// published <see cref="IScorecardSnapshotReader"/> port, the read the dossier uses, so a row on the universe
/// screen can never disagree with the dossier's verdict (FR-002: no parallel scoring formula). A symbol whose
/// scorecard cannot be read comes back as a failed row, so one bad name never blanks the rest. Lives in the host so
/// neither module references the other.
/// </summary>
public sealed class ResearchUniverseScorecardAdapter(
    IScorecardSnapshotReader snapshots,
    ILogger<ResearchUniverseScorecardAdapter> logger) : IUniverseScorecardSource
{
    public const string UnreadableReason = "the scorecard could not be read";

    public async Task<IReadOnlyList<UniverseScorecard>> GetAsync(IReadOnlyList<string> symbols, CancellationToken ct)
    {
        var result = new List<UniverseScorecard>(symbols.Count);

        // One after another: the scorecard reads share this request's scoped DbContext, which allows a single
        // operation at a time (as GetScorecardVerdictsQuery does).
        foreach (var symbol in symbols)
        {
            try
            {
                result.Add(ToUniverseScorecard(await snapshots.GetAsync(symbol, ct)));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Scorecard for {Symbol} could not be read; the universe row shows the failure", symbol);
                result.Add(UniverseScorecard.Failed(symbol, UnreadableReason));
            }
        }

        return result;
    }

    public static UniverseScorecard ToUniverseScorecard(ScorecardSnapshot s) =>
        new(
            s.Symbol,
            null,
            s.GeneratedAt,
            s.StructureRaw,
            s.StructureAdjusted,
            s.StructureBasis,
            s.StructureNotEvaluable,
            s.Fundamentals,
            s.FundamentalsNotEvaluable,
            s.Crowding,
            s.ExtensionFromMa50,
            s.ValuationNotApplicable,
            s.TrailingPe,
            s.TrailingPeFiveYearAvg,
            s.ValuationPremiumPct,
            s.ImpliedUpsidePct,
            s.ValuationNotEvaluable,
            s.ValuationStale,
            s.VerdictWord,
            s.RulesVersion,
            s.FiredRuleId,
            s.FiredRuleDescription);
}
