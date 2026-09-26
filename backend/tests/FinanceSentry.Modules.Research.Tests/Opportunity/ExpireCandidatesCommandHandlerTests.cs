namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FluentAssertions;
using Xunit;

/// <summary>
/// S3 (#419): expiring a candidate resolves its Opportunity alert (the reference id is the candidate's
/// own Id, so a re-nomination under the same ticker later raises a fresh row rather than reopening
/// this one).
/// </summary>
public sealed class ExpireCandidatesCommandHandlerTests
{
    private static ExpireCandidatesCommandHandler BuildHandler(
        FakeCandidateRepository candidateRepo,
        FakeCandidateScoreRepository scoreRepo,
        RecordingThesisEventRecorder eventRecorder,
        FakeOpportunityAlertGenerator alerts)
        => new(candidateRepo, scoreRepo, eventRecorder, alerts);

    [Fact]
    public async Task Handle_ExpiredCandidate_ResolvesItsOpportunityAlert()
    {
        var candidateRepo = new FakeCandidateRepository();
        var scoreRepo = new FakeCandidateScoreRepository();
        var eventRecorder = new RecordingThesisEventRecorder();
        var alerts = new FakeOpportunityAlertGenerator();

        var userId = Guid.NewGuid();
        var candidate = new OpportunityCandidate
        {
            UserId = userId,
            Ticker = "AAPL",
            Status = CandidateStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        candidateRepo.Candidates.Add(candidate);

        var handler = BuildHandler(candidateRepo, scoreRepo, eventRecorder, alerts);

        var result = await handler.Handle(new ExpireCandidatesCommand(DateTimeOffset.UtcNow), CancellationToken.None);

        result.ExpiredCount.Should().Be(1);
        alerts.ResolvedOpportunityAlerts.Should().ContainSingle()
            .Which.Should().Be((userId, candidate.Id));
    }

    [Fact]
    public async Task Handle_NoExpiredCandidates_ResolvesNothing()
    {
        var candidateRepo = new FakeCandidateRepository();
        var scoreRepo = new FakeCandidateScoreRepository();
        var eventRecorder = new RecordingThesisEventRecorder();
        var alerts = new FakeOpportunityAlertGenerator();

        var candidate = new OpportunityCandidate
        {
            UserId = Guid.NewGuid(),
            Ticker = "AAPL",
            Status = CandidateStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        };
        candidateRepo.Candidates.Add(candidate);

        var handler = BuildHandler(candidateRepo, scoreRepo, eventRecorder, alerts);

        var result = await handler.Handle(new ExpireCandidatesCommand(DateTimeOffset.UtcNow), CancellationToken.None);

        result.ExpiredCount.Should().Be(0);
        alerts.ResolvedOpportunityAlerts.Should().BeEmpty();
    }
}
