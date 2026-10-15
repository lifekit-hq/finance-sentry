namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using Microsoft.Extensions.Options;

/// <summary>
/// Reads one symbol's scorecard and its verdict at request time (spec 726 US2): the unchanged 019 scorers
/// via <see cref="ScorecardComposer"/>, the 021 regime adjustment, the caller's valuation facts, then
/// <see cref="VerdictRules"/>. Shared corpus: nothing per-user, nothing persisted, no candidate, signal or
/// alert side effects (those stay with candidate scoring).
/// </summary>
public sealed class ScorecardReader(
    IMarketStructureReader structureReader,
    IFundamentalsService fundamentals,
    IMarketRegimeSource regimeSource,
    IOptions<OpportunityOptions> opportunityOptions,
    IOptions<VerdictOptions> verdictOptions)
{
    public const string NoExtensionData = "no_extension_data";

    /// <summary>
    /// The scorecard for <paramref name="symbol"/>. <paramref name="facts"/> reuses fundamentals the caller
    /// already read (at least <see cref="FundamentalsScorer.FactsPerConcept"/> per concept); null reads them.
    /// A name outside the v1 universe skips the fundamentals read, since its verdict is NOT SCORED anyway.
    /// </summary>
    public async Task<ScorecardResult> ReadAsync(
        string symbol,
        ScorecardValuationAxis valuation,
        IReadOnlyList<FundamentalFact>? facts,
        CancellationToken ct)
    {
        var structure = await structureReader.GetStructureAsync(symbol, ct);
        facts ??= valuation.NotApplicable
            ? []
            : (await fundamentals.GetFundamentalsAsync(symbol, FundamentalsScorer.FactsPerConcept, ct)).Facts;
        var regime = await regimeSource.GetLatestAsync(ct);

        var composed = ScorecardComposer.Compose(structure, facts, regime, opportunityOptions.Value);
        return Build(symbol, composed, valuation, opportunityOptions.Value, verdictOptions.Value, DateTimeOffset.UtcNow);
    }

    /// <summary>Projects a composed scorecard and its valuation facts into the published shape and verdict.</summary>
    public static ScorecardResult Build(
        string symbol,
        ComposedScorecard composed,
        ScorecardValuationAxis valuation,
        OpportunityOptions opportunity,
        VerdictOptions verdictOptions,
        DateTimeOffset generatedAt)
    {
        var evidence = composed.Evidence;
        var regime = composed.Regime;

        // The classifier reads a missing extension as Normal; the verdict must not (FR-008).
        var crowding = evidence.ExtensionFromMa50 is null ? (CrowdingClass?)null : composed.Crowding;

        var verdict = VerdictRules.Evaluate(
            new VerdictInputs(
                regime.AdjustedStructureScore,
                composed.FundamentalsScore,
                crowding,
                valuation.NotApplicable,
                valuation.PremiumPct),
            verdictOptions);
        var structureBasis = regime.Rationale.Contains(RegimeScoreAdjuster.NoRegimeData)
            ? VerdictDto.RawBasis
            : VerdictDto.AdjustedBasis;

        return new ScorecardResult(
            symbol,
            generatedAt,
            opportunity.FormulaVersion,
            new ScorecardStructureAxis(
                composed.StructureScore,
                regime.AdjustedStructureScore,
                regime.AdjustmentPoints,
                regime.Rationale,
                regime.VolatilityRegime,
                regime.RatesRegime,
                regime.AsOf,
                evidence.RsByWindow,
                evidence.ReturnByWindow,
                evidence.ExtensionFromMa50,
                evidence.TodayZScore,
                evidence.VolumeRatio,
                evidence.SectorRank,
                evidence.SectorRankDelta,
                evidence.DistanceFrom63dHigh,
                evidence.StructureNotEvaluableReasons),
            new ScorecardFundamentalsAxis(
                composed.FundamentalsScore,
                evidence.RevenueYoy,
                evidence.GrossMarginLatest,
                evidence.GrossMarginTrend,
                evidence.EpsYoy,
                evidence.FundamentalsBasis,
                evidence.FundamentalsNotEvaluableReasons),
            new ScorecardCrowdingAxis(
                crowding?.ToString(),
                evidence.ExtensionFromMa50,
                evidence.VolumeRatio,
                opportunity.EarlyExtensionThreshold,
                opportunity.ExtendedExtensionThreshold,
                opportunity.ExtendedVolumeRatioThreshold,
                crowding is null ? NoExtensionData : null),
            valuation,
            VerdictDto.From(verdict, structureBasis));
    }
}
