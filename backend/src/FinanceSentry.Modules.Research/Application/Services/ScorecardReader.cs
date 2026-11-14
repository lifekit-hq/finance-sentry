namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services.Etf;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using Microsoft.Extensions.Options;

/// <summary>
/// Reads one symbol's scorecard and its verdict at request time (spec 726 US2): the unchanged 019 scorers
/// via <see cref="ScorecardComposer"/>, the 021 regime adjustment, the caller's valuation facts, then
/// <see cref="VerdictRules"/>. Shared corpus: nothing per-user, nothing persisted, no candidate, signal or
/// alert side effects (those stay with candidate scoring). A non-equity that turns out to be an ETF is read through its
/// top 10 holdings instead (issue #1324): <see cref="EtfLookThroughReader"/> scores each holding by these same rules and
/// <see cref="EtfVerdictRules"/> gives the fund its word.
/// </summary>
public sealed class ScorecardReader(
    IMarketStructureReader structureReader,
    IFundamentalsService fundamentals,
    IMarketRegimeSource regimeSource,
    IOptions<OpportunityOptions> opportunityOptions,
    IOptions<VerdictOptions> verdictOptions,
    EtfLookThroughReader? etfLookThrough = null)
{
    public const string NoExtensionData = "no_extension_data";

    private const int PercentScale = 100;

    /// <summary>
    /// The scorecard for <paramref name="symbol"/>. <paramref name="facts"/> reuses fundamentals the caller
    /// already read (at least <see cref="FundamentalsScorer.FactsPerConcept"/> per concept); null reads them.
    /// A name outside the v1 universe skips the fundamentals read, since its verdict is NOT SCORED anyway.
    /// <paramref name="peerForwardPes"/> are the peers' forward P/Es for the fallback price lens; it applies only
    /// where the own-history premium is missing, and null means no peers were read.
    /// <paramref name="lookThrough"/> false keeps a non-equity at NOT SCORED instead of asking whether it is an ETF: the
    /// dossier turns it off for a crypto position, and the look-through turns it off for each holding it scores.
    /// </summary>
    public async Task<ScorecardResult> ReadAsync(
        string symbol,
        ScorecardValuationAxis valuation,
        IReadOnlyList<FundamentalFact>? facts,
        CancellationToken ct,
        string? sector = null,
        string? industry = null,
        IReadOnlyList<decimal?>? peerForwardPes = null,
        bool lookThrough = true)
    {
        var structure = await structureReader.GetStructureAsync(symbol, ct);
        facts ??= valuation.NotApplicable
            ? []
            : (await fundamentals.GetFundamentalsAsync(symbol, FundamentalsScorer.FactsPerConcept, ct)).Facts;
        var regime = await regimeSource.GetLatestAsync(ct);

        var composed = ScorecardComposer.Compose(structure, facts, regime, opportunityOptions.Value);
        var options = verdictOptions.Value;
        if (peerForwardPes is not null)
        {
            valuation = valuation.WithPeerLens(peerForwardPes, options.MinPeersForPriceLens);
        }

        if (valuation.NotApplicable && lookThrough && etfLookThrough is not null)
        {
            var etf = await etfLookThrough.ReadAsync(
                symbol,
                (holding, axis, holdingSector, holdingIndustry, holdingPeers, token) =>
                    ReadAsync(holding, axis, null, token, holdingSector, holdingIndustry, holdingPeers, lookThrough: false),
                options.EtfBuyWeightSharePctAbove,
                ct);
            if (etf is not null)
            {
                return BuildEtf(symbol, composed, valuation, opportunityOptions.Value, options, DateTimeOffset.UtcNow, etf);
            }
        }

        return Build(symbol, composed, valuation, opportunityOptions.Value, options, DateTimeOffset.UtcNow, sector, industry);
    }

    /// <summary>Projects a composed scorecard and its valuation facts into the published shape and verdict.</summary>
    public static ScorecardResult Build(
        string symbol,
        ComposedScorecard composed,
        ScorecardValuationAxis valuation,
        OpportunityOptions opportunity,
        VerdictOptions verdictOptions,
        DateTimeOffset generatedAt,
        string? sector = null,
        string? industry = null)
    {
        var crowding = ReadCrowding(composed);
        var verdict = VerdictRules.Evaluate(
            new VerdictInputs(
                composed.Regime.AdjustedStructureScore,
                composed.FundamentalsScore,
                crowding,
                valuation.NotApplicable,
                valuation.LensPremium(),
                evidence.RevenueYoy is { } growth ? decimal.Round(growth * PercentScale, 1) : null,
                valuation.PriceLens),
            verdictOptions);
        return Assemble(symbol, composed, valuation, opportunity, crowding, verdict, generatedAt, sector, industry, null);
    }

    /// <summary>
    /// An ETF's scorecard (issue #1324): the fund's own structure and crowding, the look-through basis, and the word
    /// from <see cref="EtfVerdictRules"/>. Fundamentals and valuation are read as for any non-equity: not applicable.
    /// </summary>
    public static ScorecardResult BuildEtf(
        string symbol,
        ComposedScorecard composed,
        ScorecardValuationAxis valuation,
        OpportunityOptions opportunity,
        VerdictOptions verdictOptions,
        DateTimeOffset generatedAt,
        EtfLookThroughRead etf)
    {
        var crowding = ReadCrowding(composed);
        var verdict = EtfVerdictRules.Evaluate(
            new EtfVerdictInputs(composed.Regime.AdjustedStructureScore, crowding, etf.BuyWeightSharePct),
            verdictOptions);
        return Assemble(symbol, composed, valuation, opportunity, crowding, verdict, generatedAt, null, null, etf.Basis);
    }

    // The classifier reads a missing extension as Normal; the verdict must not (FR-008).
    private static CrowdingClass? ReadCrowding(ComposedScorecard composed) =>
        composed.Evidence.ExtensionFromMa50 is null ? null : composed.Crowding;

    private static ScorecardResult Assemble(
        string symbol,
        ComposedScorecard composed,
        ScorecardValuationAxis valuation,
        OpportunityOptions opportunity,
        CrowdingClass? crowding,
        Verdict verdict,
        DateTimeOffset generatedAt,
        string? sector,
        string? industry,
        ScorecardLookThrough? lookThrough)
    {
        var evidence = composed.Evidence;
        var regime = composed.Regime;
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
                evidence.FundamentalsNotEvaluableReasons,
                evidence.EpsYoyBasis),
            new ScorecardCrowdingAxis(
                crowding?.ToString(),
                evidence.ExtensionFromMa50,
                evidence.VolumeRatio,
                opportunity.EarlyExtensionThreshold,
                opportunity.ExtendedExtensionThreshold,
                opportunity.ExtendedVolumeRatioThreshold,
                crowding is null ? NoExtensionData : null),
            valuation,
            VerdictDto.From(verdict, structureBasis),
            sector,
            industry,
            lookThrough);
    }
}
