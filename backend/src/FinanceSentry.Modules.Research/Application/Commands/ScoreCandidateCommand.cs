namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.Extensions.Options;

public record ScoreCandidateCommand(
    Guid UserId,
    string Ticker,
    string? DecisionNote = null,
    CandidateSource Source = CandidateSource.User,
    IReadOnlyList<string>? NominationReasons = null) : ICommand<ScoreCandidateResult>;

public sealed record ScoreCandidateResult(
    Guid CandidateId,
    string Ticker,
    bool IsNewCandidate,
    CandidateScorecard Scorecard);

public sealed class ScoreCandidateCommandHandler(
    ICandidateRepository candidateRepo,
    ICandidateScoreRepository scoreRepo,
    IMarketStructureReader structureReader,
    ISecEdgarService secEdgar,
    IIpsRepository ipsRepo,
    IPositionCapSource positionCapSource,
    IBrokerageHoldingsReader holdingsReader,
    IRadarSignalWriter signalWriter,
    IMarketRegimeSource marketRegimeSource,
    IThesisEventRecorder eventRecorder,
    IAlertGeneratorService alertGenerator,
    IOptions<OpportunityOptions> options)
    : ICommandHandler<ScoreCandidateCommand, ScoreCandidateResult>
{
    private const string Scanner = "opportunity";
    private const string NominationSignalType = "candidate_scored";
    private const string TopTierSignalType = "top_tier_candidate";
    private const string SubjectTypeTicker = "Ticker";
    private const string DefaultNominationReason = "conviction";
    private const string NoIpsNote = "no IPS on file";

    private readonly OpportunityOptions _options = options.Value;

    public async Task<ScoreCandidateResult> Handle(ScoreCandidateCommand command, CancellationToken ct)
    {
        var ticker = command.Ticker.Trim().ToUpperInvariant();

        var (candidate, isNew) = await candidateRepo.UpsertActiveAsync(
            command.UserId, ticker, command.Source, TimeSpan.FromDays(_options.CandidateTtlDays), ct);

        IReadOnlyList<string> incomingReasons = command.NominationReasons is { Count: > 0 }
            ? command.NominationReasons
            : [DefaultNominationReason];
        var newReasons = incomingReasons.Where(r => !candidate.NominationReasons.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        if (newReasons.Count > 0)
        {
            candidate.NominationReasons.AddRange(newReasons);
            await candidateRepo.UpdateAsync(candidate, ct);
        }

        var structureSnapshot = await structureReader.GetStructureAsync(ticker, ct);
        var fundamentalFacts = await secEdgar.GetFundamentalsAsync(ticker, FundamentalsScorer.FactsPerConcept, ct);
        var ips = await ipsRepo.GetCurrentAsync(command.UserId, ct);
        // 039: the single-position cap now lives in its single home (the Risk rule set), read via port.
        var maxPositionCap = await positionCapSource.GetMaxPositionWeightAsync(command.UserId, ct);
        var holdings = await holdingsReader.GetHoldingsAsync(command.UserId, ct);

        var (structureScore, structureReasons) = StructureScorer.Score(structureSnapshot);
        var (fundamentalsScore, revenueYoy, grossMarginLatest, grossMarginTrend, epsYoy, fundamentalsReasons) =
            FundamentalsScorer.Score(fundamentalFacts);
        var crowding = CrowdingClassifier.Classify(
            structureSnapshot?.ExtensionFromMa50, structureSnapshot?.VolumeRatio, _options);

        var ipsFit = BuildIpsFit(ticker, ips, maxPositionCap, holdings);

        // 021: regime is CONTEXT only — haircut the presented/ranked structure score for speculative
        // candidates in risk-off macro conditions. Raw structureScore is preserved (and persisted);
        // regime never actions (no cash/sell/promotion change). Null regime ⇒ raw == adjusted.
        var regimeSnapshot = await marketRegimeSource.GetLatestAsync(ct);
        var regime = RegimeScoreAdjuster.Adjust(structureScore, crowding, regimeSnapshot, _options);

        var evidence = new ScoreEvidence(
            structureSnapshot?.RsByWindow ?? new Dictionary<int, decimal?>(),
            structureSnapshot?.ReturnByWindow ?? new Dictionary<int, decimal?>(),
            structureSnapshot?.ExtensionFromMa50,
            structureSnapshot?.TodayZScore,
            structureSnapshot?.VolumeRatio,
            structureReasons,
            revenueYoy,
            grossMarginLatest,
            grossMarginTrend,
            epsYoy,
            fundamentalsReasons,
            SectorRank: structureSnapshot?.SectorRank,
            SectorRankDelta: structureSnapshot?.SectorRankDelta,
            DistanceFrom63dHigh: structureSnapshot?.DistanceFrom63dHigh);

        var scorecard = new CandidateScorecard(
            structureScore, fundamentalsScore, crowding, ipsFit, evidence, _options.FormulaVersion, regime);

        await scoreRepo.AppendAsync(new CandidateScore
        {
            CandidateId = candidate.Id,
            // Persist the RAW structure score (formula-version integrity); the regime-adjusted score
            // is time-varying context returned on the scorecard, never the canonical persisted value.
            StructureScore = structureScore,
            FundamentalsScore = fundamentalsScore,
            CrowdingClass = crowding,
            IpsFit = ipsFit,
            Evidence = evidence,
            FormulaVersion = _options.FormulaVersion,
        }, ct);

        if (isNew)
        {
            await eventRecorder.RecordAsync(
                command.UserId, ThesisSubjectType.Candidate, candidate.Id, ticker,
                ThesisEventType.Created, command.DecisionNote, ct);
        }

        await EmitSignalsAsync(command.UserId, candidate, ticker, structureScore, fundamentalsScore, ct);

        return new ScoreCandidateResult(candidate.Id, ticker, isNew, scorecard);
    }

    private async Task EmitSignalsAsync(
        Guid userId, OpportunityCandidate candidate, string ticker, int? structureScore, int? fundamentalsScore, CancellationToken ct)
    {
        await signalWriter.AppendSignalAsync(new RadarSignalRequest(
            Scanner,
            NominationSignalType,
            SignalSeverity.Info,
            SubjectTypeTicker,
            ticker,
            userId,
            $"{Scanner}:{NominationSignalType}:{ticker}:{userId}:{DateTimeOffset.UtcNow:yyyy-MM-dd}",
            new { structureScore, fundamentalsScore }), ct);

        var isTopTier = structureScore >= _options.TopTierScoreBar || fundamentalsScore >= _options.TopTierScoreBar;
        if (!isTopTier)
        {
            return;
        }

        await signalWriter.AppendSignalAsync(new RadarSignalRequest(
            Scanner,
            TopTierSignalType,
            SignalSeverity.Notable,
            SubjectTypeTicker,
            ticker,
            userId,
            $"{Scanner}:{TopTierSignalType}:{ticker}:{userId}",
            new { structureScore, fundamentalsScore }), ct);

        var reason = $"structure {structureScore?.ToString() ?? "n/a"}, fundamentals {fundamentalsScore?.ToString() ?? "n/a"}";
        await alertGenerator.GenerateOpportunityAlertAsync(userId, candidate.Id, ticker, reason, ct);
    }

    private static IpsFitFacts BuildIpsFit(
        string ticker,
        InvestmentPolicyStatement? ips,
        decimal? maxPositionCap,
        IReadOnlyList<BrokerageHoldingSummary> holdings)
    {
        var totalUsd = holdings.Sum(h => h.UsdValue);
        var tickerUsd = holdings.Where(h => string.Equals(h.Symbol, ticker, StringComparison.OrdinalIgnoreCase)).Sum(h => h.UsdValue);
        var currentWeight = totalUsd > 0 ? tickerUsd / totalUsd : (decimal?)null;

        if (ips is null)
        {
            return new IpsFitFacts(currentWeight, null, null, true, "unknown", NoIpsNote);
        }

        // 039: cap sourced from the Risk rule set (single home), fraction-vs-fraction comparison —
        // consistent with Risk enforcement.
        var withinConcentration = currentWeight is null
            || maxPositionCap is null
            || currentWeight <= maxPositionCap;

        var assetClassFit = ips.Exclusions.Any(e => string.Equals(e, ticker, StringComparison.OrdinalIgnoreCase))
            ? "excluded_by_ips"
            : "not_classified";

        return new IpsFitFacts(currentWeight, null, maxPositionCap, withinConcentration, assetClassFit);
    }
}
