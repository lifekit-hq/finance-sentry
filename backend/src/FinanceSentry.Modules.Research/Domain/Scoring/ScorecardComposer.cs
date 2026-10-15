namespace FinanceSentry.Modules.Research.Domain.Scoring;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Opportunity;

/// <summary>
/// The per-user-independent half of a 019 scorecard: the three scorers run unchanged over the structure
/// snapshot and fundamentals facts, the 021 regime adjustment, and the <see cref="ScoreEvidence"/> behind
/// them. <see cref="StructureScore"/> is the raw score; <see cref="Regime"/> carries the adjusted one.
/// </summary>
public sealed record ComposedScorecard(
    int? StructureScore,
    int? FundamentalsScore,
    CrowdingClass Crowding,
    ScoreEvidence Evidence,
    RegimeAdjustment Regime);

/// <summary>
/// Composes the scorer outputs and their evidence from already-read inputs. Shared by candidate scoring
/// (which adds IPS fit and persists) and the read-only scorecard (spec 726 US2), so there is one way to
/// build a scorecard and no parallel formula (spec 726 FR-002).
/// </summary>
public static class ScorecardComposer
{
    public static ComposedScorecard Compose(
        MarketStructureSnapshot? structure,
        IReadOnlyList<FundamentalFact> facts,
        MarketRegimeSnapshot? regime,
        OpportunityOptions options)
    {
        var (structureScore, structureReasons) = StructureScorer.Score(structure);
        var (fundamentalsScore, revenueYoy, grossMarginLatest, grossMarginTrend, epsYoy, fundamentalsReasons, fundamentalsBasis) =
            FundamentalsScorer.Evaluate(facts);
        var crowding = CrowdingClassifier.Classify(structure?.ExtensionFromMa50, structure?.VolumeRatio, options);

        // 021: regime is CONTEXT only - haircut the presented/ranked structure score for speculative
        // candidates in risk-off macro conditions. Raw structureScore is preserved; regime never actions
        // (no cash/sell/promotion change). Null regime ⇒ raw == adjusted.
        var adjustment = RegimeScoreAdjuster.Adjust(structureScore, crowding, regime, options);

        var evidence = new ScoreEvidence(
            structure?.RsByWindow ?? new Dictionary<int, decimal?>(),
            structure?.ReturnByWindow ?? new Dictionary<int, decimal?>(),
            structure?.ExtensionFromMa50,
            structure?.TodayZScore,
            structure?.VolumeRatio,
            structureReasons,
            revenueYoy,
            grossMarginLatest,
            grossMarginTrend,
            epsYoy,
            fundamentalsReasons,
            SectorRank: structure?.SectorRank,
            SectorRankDelta: structure?.SectorRankDelta,
            DistanceFrom63dHigh: structure?.DistanceFrom63dHigh,
            FundamentalsBasis: fundamentalsBasis);

        return new ComposedScorecard(structureScore, fundamentalsScore, crowding, evidence, adjustment);
    }
}
