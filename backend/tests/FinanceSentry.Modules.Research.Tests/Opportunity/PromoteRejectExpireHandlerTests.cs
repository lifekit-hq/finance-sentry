namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class PromoteRejectExpireHandlerTests
{
    private sealed class RecordingSaveThesisHandler : ICommandHandler<SaveThesisCommand, ThesisDto>
    {
        public int Calls { get; private set; }
        public Guid ThesisId { get; } = Guid.NewGuid();

        public Task<ThesisDto> Handle(SaveThesisCommand cmd, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new ThesisDto(
                ThesisId, cmd.Ticker, cmd.ThesisText, cmd.KeyDataPoints, cmd.Catalysts,
                cmd.InvalidationTriggers, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null));
        }
    }

    private static OpportunityCandidate SeedActive(FakeCandidateRepository repo, Guid userId, string ticker)
    {
        var candidate = new OpportunityCandidate
        {
            UserId = userId,
            Ticker = ticker,
            Source = CandidateSource.User,
            Status = CandidateStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        };
        repo.Candidates.Add(candidate);
        return candidate;
    }

    private static PromoteCandidateCommandHandler BuildPromote(
        FakeCandidateRepository candidates,
        FakeCandidateScoreRepository scores,
        RiskGateVerdict verdict,
        RecordingSaveThesisHandler save,
        RecordingRadarSignalWriter signals)
        => new(
            candidates,
            scores,
            new FakeRiskPolicyGate(verdict),
            signals,
            new RecordingThesisEventRecorder(),
            save,
            Options.Create(new OpportunityOptions()));

    [Fact]
    public async Task Promote_RefusedByGate_CreatesNoThesis_AndReturnsVerdict()
    {
        var candidates = new FakeCandidateRepository();
        var userId = Guid.NewGuid();
        var candidate = SeedActive(candidates, userId, "MSFT");

        var refused = new RiskGateVerdict(RiskGateDecision.Refused, "max_position_weight", 0.4m, 0.25m, 5_000m, "too big");
        var save = new RecordingSaveThesisHandler();
        var handler = BuildPromote(candidates, new FakeCandidateScoreRepository(), refused, save, new RecordingRadarSignalWriter());

        var result = await handler.Handle(
            new PromoteCandidateCommand(userId, candidate.Id, ProposedUsd: 9_000m), CancellationToken.None);

        result.ThesisId.Should().BeNull();
        result.Gate.Decision.Should().Be(RiskGateDecision.Refused);
        save.Calls.Should().Be(0);
        candidate.Status.Should().Be(CandidateStatus.Active);
    }

    [Fact]
    public async Task Promote_AllowedByGate_CreatesThesis_AndMarksPromoted()
    {
        var candidates = new FakeCandidateRepository();
        var userId = Guid.NewGuid();
        var candidate = SeedActive(candidates, userId, "MSFT");

        var allowed = new RiskGateVerdict(RiskGateDecision.Allowed, null, null, null, null, "no rules on file");
        var save = new RecordingSaveThesisHandler();
        var handler = BuildPromote(candidates, new FakeCandidateScoreRepository(), allowed, save, new RecordingRadarSignalWriter());

        var result = await handler.Handle(
            new PromoteCandidateCommand(userId, candidate.Id), CancellationToken.None);

        save.Calls.Should().Be(1);
        result.ThesisId.Should().Be(save.ThesisId);
        candidate.Status.Should().Be(CandidateStatus.Promoted);
        candidate.PromotedThesisId.Should().Be(save.ThesisId);
    }

    [Fact]
    public async Task Promote_RefusedWithOverride_RecordsOverrideSignal_AndCreatesThesis()
    {
        var candidates = new FakeCandidateRepository();
        var userId = Guid.NewGuid();
        var candidate = SeedActive(candidates, userId, "MSFT");

        var refused = new RiskGateVerdict(RiskGateDecision.Refused, "max_position_weight", 0.4m, 0.25m, 5_000m, "too big");
        var save = new RecordingSaveThesisHandler();
        var signals = new RecordingRadarSignalWriter();
        var handler = BuildPromote(candidates, new FakeCandidateScoreRepository(), refused, save, signals);

        var result = await handler.Handle(
            new PromoteCandidateCommand(userId, candidate.Id, OverrideRisk: true, ProposedUsd: 9_000m),
            CancellationToken.None);

        result.ThesisId.Should().Be(save.ThesisId);
        save.Calls.Should().Be(1);
        signals.Signals.Should().ContainSingle(s => s.SignalType == "promotion_risk_override");
    }

    [Fact]
    public async Task Promote_Paper_PassesIsPaperToGate_AndRecordsNoOverrideWhenAllowed()
    {
        var candidates = new FakeCandidateRepository();
        var userId = Guid.NewGuid();
        var candidate = SeedActive(candidates, userId, "MSFT");

        // Simulates the real-book MinCashBuffer rule being skipped for a paper promotion: the gate
        // itself (RiskEvaluationService, see RiskEvaluationServiceTests) is what applies isPaper —
        // this fake only proves PromoteCandidateCommand.Paper reaches IRiskPolicyGate.CheckProposalAsync.
        var allowed = new RiskGateVerdict(RiskGateDecision.Allowed, null, null, null, null, "paper: cash rule skipped");
        var save = new RecordingSaveThesisHandler();
        var signals = new RecordingRadarSignalWriter();
        var gate = new FakeRiskPolicyGate(allowed);
        var handler = new PromoteCandidateCommandHandler(
            candidates,
            new FakeCandidateScoreRepository(),
            gate,
            signals,
            new RecordingThesisEventRecorder(),
            save,
            Options.Create(new OpportunityOptions()));

        var result = await handler.Handle(
            new PromoteCandidateCommand(userId, candidate.Id, ProposedUsd: 9_000m, Paper: true),
            CancellationToken.None);

        gate.LastIsPaper.Should().BeTrue();
        result.ThesisId.Should().Be(save.ThesisId);
        signals.Signals.Should().BeEmpty("no override was needed or recorded for an allowed paper promotion");
    }

    [Fact]
    public async Task Reject_MarksRejected_WithReason()
    {
        var candidates = new FakeCandidateRepository();
        var userId = Guid.NewGuid();
        var candidate = SeedActive(candidates, userId, "MSFT");

        var events = new RecordingThesisEventRecorder();
        var handler = new RejectCandidateCommandHandler(candidates, events);

        var result = await handler.Handle(
            new RejectCandidateCommand(userId, candidate.Id, "valuation too rich"), CancellationToken.None);

        result.Status.Should().Be(CandidateStatus.Rejected);
        events.Events.Should().Contain(ThesisEventType.Rejected);
        candidate.RejectedReason.Should().Be("valuation too rich");
    }

    [Fact]
    public async Task Expire_MarksExpired_AndAppendsFinalScoreSnapshot()
    {
        var candidates = new FakeCandidateRepository();
        var scores = new FakeCandidateScoreRepository();
        var userId = Guid.NewGuid();

        var candidate = new OpportunityCandidate
        {
            UserId = userId,
            Ticker = "MSFT",
            Source = CandidateSource.User,
            Status = CandidateStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        candidates.Candidates.Add(candidate);
        scores.Scores.Add(new CandidateScore
        {
            CandidateId = candidate.Id,
            StructureScore = 60,
            FundamentalsScore = 70,
        });

        var events = new RecordingThesisEventRecorder();
        var handler = new ExpireCandidatesCommandHandler(candidates, scores, events, new FakeOpportunityAlertGenerator());

        var result = await handler.Handle(
            new ExpireCandidatesCommand(DateTimeOffset.UtcNow), CancellationToken.None);

        result.ExpiredCount.Should().Be(1);
        events.Events.Should().Contain(ThesisEventType.Expired);
        candidate.Status.Should().Be(CandidateStatus.Expired);

        // Final snapshot appended → two score rows for the candidate.
        scores.Scores.Count(s => s.CandidateId == candidate.Id).Should().Be(2);
    }
}
