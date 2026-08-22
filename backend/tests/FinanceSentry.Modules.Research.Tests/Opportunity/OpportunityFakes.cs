namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>Shared deterministic test doubles for the opportunity lifecycle handler tests.</summary>
internal sealed class FakeCandidateRepository : ICandidateRepository
{
    public List<OpportunityCandidate> Candidates { get; } = [];

    public Task<(OpportunityCandidate Candidate, bool IsNew)> UpsertActiveAsync(
        Guid userId, string ticker, CandidateSource source, TimeSpan ttl, CancellationToken ct = default)
    {
        var existing = Candidates.FirstOrDefault(c =>
            c.UserId == userId && c.Ticker == ticker && c.Status == CandidateStatus.Active);
        if (existing is not null)
        {
            return Task.FromResult((existing, false));
        }

        var candidate = new OpportunityCandidate
        {
            UserId = userId,
            Ticker = ticker,
            Source = source,
            Status = CandidateStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow + ttl,
        };
        Candidates.Add(candidate);
        return Task.FromResult((candidate, true));
    }

    public Task<OpportunityCandidate?> FindActiveByTickerAsync(Guid userId, string ticker, CancellationToken ct = default)
        => Task.FromResult(Candidates.FirstOrDefault(c =>
            c.UserId == userId && c.Ticker == ticker && c.Status == CandidateStatus.Active));

    public Task<OpportunityCandidate?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
        => Task.FromResult(Candidates.FirstOrDefault(c => c.UserId == userId && c.Id == id));

    public Task<IReadOnlyList<OpportunityCandidate>> ListAsync(
        Guid userId, CandidateStatus? status = null, CandidateSource? source = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OpportunityCandidate>>(Candidates
            .Where(c => c.UserId == userId
                && (status is null || c.Status == status)
                && (source is null || c.Source == source))
            .OrderByDescending(c => c.CreatedAt)
            .ToList());

    public Task<IReadOnlyList<OpportunityCandidate>> ListExpiredAsync(DateTimeOffset asOf, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OpportunityCandidate>>(Candidates
            .Where(c => c.Status == CandidateStatus.Active && c.ExpiresAt <= asOf)
            .ToList());

    public Task UpdateAsync(OpportunityCandidate candidate, CancellationToken ct = default)
    {
        if (!Candidates.Contains(candidate))
        {
            Candidates.Add(candidate);
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeCandidateScoreRepository : ICandidateScoreRepository
{
    public List<CandidateScore> Scores { get; } = [];

    public Task AppendAsync(CandidateScore score, CancellationToken ct = default)
    {
        Scores.Add(score);
        return Task.CompletedTask;
    }

    public Task<CandidateScore?> LatestForCandidateAsync(Guid candidateId, CancellationToken ct = default)
        => Task.FromResult(Scores
            .Where(s => s.CandidateId == candidateId)
            .OrderByDescending(s => s.ScoredAt)
            .FirstOrDefault());
}

internal sealed class FakeMarketStructureReader(MarketStructureSnapshot? snapshot = null) : IMarketStructureReader
{
    public Task<MarketStructureSnapshot?> GetStructureAsync(string ticker, CancellationToken ct = default)
        => Task.FromResult(snapshot);

    public Task<IReadOnlyList<PairwiseCorrelation>> GetPairwiseCorrelationsAsync(
        IReadOnlyCollection<string> tickers, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PairwiseCorrelation>>([]);

    public Task<IReadOnlyList<UniverseStructureEntry>> GetUniverseStructuresAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UniverseStructureEntry>>(
            snapshot is null ? [] : [new UniverseStructureEntry(snapshot.Ticker, false, snapshot)]);
}

internal sealed class FakeSecEdgarService(IReadOnlyList<FundamentalFact>? facts = null) : ISecEdgarService
{
    public Task<IReadOnlyList<EdgarFiling>> GetRecentFilingsAsync(
        string ticker, IReadOnlyCollection<string>? formTypes, int limit, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EdgarFiling>>([]);

    public Task<IReadOnlyList<FundamentalFact>> GetFundamentalsAsync(
        string ticker, int maxPerConcept, CancellationToken ct = default)
        => Task.FromResult(facts ?? []);
}

internal sealed class FakeIpsRepository(InvestmentPolicyStatement? ips = null) : Domain.Repositories.IIpsRepository
{
    public Task<InvestmentPolicyStatement?> GetCurrentAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(ips);

    public Task<IReadOnlyList<InvestmentPolicyStatement>> ListVersionsAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<InvestmentPolicyStatement>>(ips is null ? [] : [ips]);

    public Task<int> GetMaxVersionAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(ips?.Version ?? 0);

    public Task AddVersionAsync(InvestmentPolicyStatement statement, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<Guid>> GetUserIdsWithCurrentIpsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Guid>>(ips is null ? [] : [ips.UserId]);
}

internal sealed class FakeBrokerageHoldingsReader(IReadOnlyList<BrokerageHoldingSummary>? holdings = null)
    : IBrokerageHoldingsReader
{
    public Task<IReadOnlyList<BrokerageHoldingSummary>> GetHoldingsAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(holdings ?? []);
}

internal sealed class RecordingRadarSignalWriter : IRadarSignalWriter
{
    public List<RadarSignalRequest> Signals { get; } = [];

    public Task<bool> AppendSignalAsync(RadarSignalRequest request, CancellationToken ct = default)
    {
        Signals.Add(request);
        return Task.FromResult(true);
    }
}

internal sealed class RecordingThesisEventRecorder : IThesisEventRecorder
{
    public List<ThesisEventType> Events { get; } = [];

    public Task RecordAsync(
        Guid userId, ThesisSubjectType subjectType, Guid subjectId, string ticker,
        ThesisEventType eventType, string? decisionNote = null, CancellationToken ct = default)
    {
        Events.Add(eventType);
        return Task.CompletedTask;
    }
}

internal sealed class FakeOpportunityAlertGenerator : IAlertGeneratorService
{
    public int OpportunityAlertCalls { get; private set; }

    public Task GenerateOpportunityAlertAsync(
        Guid userId, Guid referenceId, string ticker, string reason, CancellationToken ct = default)
    {
        OpportunityAlertCalls++;
        return Task.CompletedTask;
    }

    public Task GenerateConsentExpiringAlertAsync(Guid userId, Guid referenceId, string providerName, DateTime expiresAt, CancellationToken ct = default) => Task.CompletedTask;
    public Task GenerateJobFailureAlertAsync(Guid userId, Guid referenceId, string jobName, int consecutiveCount, string? lastError, CancellationToken ct = default) => Task.CompletedTask;
    public Task GenerateLowBalanceAlertAsync(Guid userId, Guid accountId, string accountName, decimal balance, decimal threshold, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResolveLowBalanceAlertAsync(Guid userId, Guid accountId, CancellationToken ct = default) => Task.CompletedTask;
    public Task GenerateSyncFailureAlertAsync(Guid userId, string provider, Guid? accountId, string? accountName, string? errorCode, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResolveSyncFailureAlertAsync(Guid userId, string provider, Guid? accountId, CancellationToken ct = default) => Task.CompletedTask;
    public Task GenerateUnusualSpendAlertAsync(Guid userId, string category, decimal currentMonthSpend, decimal averageMonthlySpend, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAlertsForAccountAsync(Guid accountId, CancellationToken ct = default) => Task.CompletedTask;
    public Task GenerateThesisBreakAlertAsync(Guid userId, Guid thesisId, string ticker, string reason, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResolveThesisBreakAlertAsync(Guid userId, Guid thesisId, CancellationToken ct = default) => Task.CompletedTask;
    public Task GenerateMarketStructureAlertAsync(Guid userId, Guid referenceId, string ticker, string reason, CancellationToken ct = default) => Task.CompletedTask;
    public Task GenerateMarketStructureFreshnessAlertAsync(Guid userId, Guid referenceId, string reason, CancellationToken ct = default) => Task.CompletedTask;
    public Task GeneratePolicyViolationAlertAsync(Guid userId, string ruleKey, string subject, decimal observedValue, decimal limitValue, bool isOverride = false, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResolvePolicyViolationAlertAsync(Guid userId, string ruleKey, string subject, CancellationToken ct = default) => Task.CompletedTask;
    public Task GeneratePerformanceBriefAlertAsync(Guid userId, string headline, string body, CancellationToken ct = default) => Task.CompletedTask;
    public Task GenerateCashShortfallAlertAsync(Guid userId, Guid accountId, string accountName, DateOnly shortfallDate, decimal shortfallAmount, string currency, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResolveCashShortfallAlertAsync(Guid userId, Guid accountId, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeRiskPolicyGate(RiskGateVerdict verdict) : IRiskPolicyGate
{
    public string? LastTicker { get; private set; }

    public Task<RiskGateVerdict> CheckProposalAsync(
        Guid userId, string ticker, decimal proposedUsd, bool overrideFlag, CancellationToken ct = default)
    {
        LastTicker = ticker;
        return Task.FromResult(verdict);
    }
}
