namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>#986: each paper pick gets one price stamp at its 30-day horizon, never a late one.</summary>
public sealed class StampPaperPickHorizonsCommandTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 10, 19, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeCandidateRepository _candidates = new();
    private readonly RecordingThesisEventRecorder _recorder = new();
    private readonly Mock<IThesisEventRepository> _events = new();

    private OpportunityCandidate Pick(int daysAgo, params (ThesisEventType Type, int AfterDays)[] journal)
    {
        var c = new OpportunityCandidate
        {
            UserId = Guid.NewGuid(), Ticker = "AAPL", Status = CandidateStatus.Expired, CreatedAt = AsOf.AddDays(-daysAgo),
        };
        _candidates.Candidates.Add(c);
        _events.Setup(e => e.ListUnscopedAsync(c.UserId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(journal
            .Select(j => new ThesisEvent
            {
                UserId = c.UserId, SubjectType = ThesisSubjectType.Candidate, SubjectId = c.Id, Ticker = c.Ticker,
                EventType = j.Type, Timestamp = c.CreatedAt.AddDays(j.AfterDays), SubjectPrice = 100m, BenchmarkPrice = 500m,
            })
            .ToList());
        return c;
    }

    private Task<int> Run()
        => new StampPaperPickHorizonsCommandHandler(_candidates, _events.Object, _recorder)
            .Handle(new StampPaperPickHorizonsCommand(AsOf), CancellationToken.None);

    [Fact]
    public async Task A_pick_whose_horizon_has_come_is_stamped_with_a_snapshot()
    {
        Pick(31, (ThesisEventType.Created, 0), (ThesisEventType.Snapshot, 7));

        (await Run()).Should().Be(1);
        _recorder.Events.Should().Equal(ThesisEventType.Snapshot);
    }

    [Fact]
    public async Task A_pick_already_stamped_at_its_horizon_is_left_alone()
    {
        Pick(31, (ThesisEventType.Created, 0), (ThesisEventType.Expired, 30));

        (await Run()).Should().Be(0);
        _recorder.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task A_pick_with_no_entry_stamp_is_skipped()
    {
        Pick(31);

        (await Run()).Should().Be(0);
    }

    [Fact]
    public async Task Picks_inside_their_window_or_past_the_grace_days_are_not_stamped()
    {
        Pick(10, (ThesisEventType.Created, 0));
        Pick(40, (ThesisEventType.Created, 0));

        (await Run()).Should().Be(0);
        _recorder.Events.Should().BeEmpty();
    }
}
