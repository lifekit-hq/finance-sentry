namespace FinanceSentry.Modules.Research.Tests.Recommendations;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public sealed class PostmortemPacketRecommendationTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static ThesisEvent Event(ThesisSubjectType subject, Guid subjectId, string ticker, ThesisEventType type)
        => new()
        {
            UserId = User, SubjectType = subject, SubjectId = subjectId, Ticker = ticker, EventType = type,
            Timestamp = At, SubjectPrice = 10m, BenchmarkPrice = 500m,
        };

    private static async Task<PostmortemPacketDto> PacketAsync(
        params ThesisEvent[] events)
    {
        var repo = new Mock<IThesisEventRepository>();
        repo.Setup(r => r.ListForPeriodAsync(User, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);
        repo.Setup(r => r.ListAsync(User, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid? subjectId, CancellationToken _) =>
                events.Where(e => e.SubjectId == subjectId).ToList());
        var calculator = new Mock<IThesisPerformanceCalculator>();
        calculator.Setup(c => c.Calculate(It.IsAny<ThesisPerformanceInput>()))
            .Returns((ThesisPerformanceInput i) => new ThesisPerformanceResult(
                i.SubjectId, i.FromEvent, i.ToEvent, 1m, 0m, 1m, 1m, 1m, true, null, "stamped"));
        var signals = new Mock<IRadarSignalReader>();
        signals.Setup(s => s.ListOverrideSignalsAsync(User, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RadarSignalRecord>());

        var handler = new GetPostmortemPacketQueryHandler(
            repo.Object, calculator.Object, signals.Object, Options.Create(new FrictionConfig()));
        return await handler.Handle(
            new GetPostmortemPacketQuery(User, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), CancellationToken.None);
    }

    [Fact]
    public async Task Recommendation_decisions_stay_out_of_the_packet_and_its_counterfactuals()
    {
        var confirmed = Guid.NewGuid();
        var rejected = Guid.NewGuid();
        var expired = Guid.NewGuid();
        var thesis = Guid.NewGuid();
        var candidate = Guid.NewGuid();

        var packet = await PacketAsync(
            Event(ThesisSubjectType.Recommendation, confirmed, "GRAB", ThesisEventType.Created),
            Event(ThesisSubjectType.Recommendation, confirmed, "GRAB", ThesisEventType.Confirmed),
            Event(ThesisSubjectType.Recommendation, rejected, "NVDA", ThesisEventType.Created),
            Event(ThesisSubjectType.Recommendation, rejected, "NVDA", ThesisEventType.Rejected),
            Event(ThesisSubjectType.Recommendation, expired, "DRAM", ThesisEventType.Created),
            Event(ThesisSubjectType.Recommendation, expired, "DRAM", ThesisEventType.Expired),
            Event(ThesisSubjectType.Thesis, thesis, "AAPL", ThesisEventType.Created),
            Event(ThesisSubjectType.Thesis, thesis, "AAPL", ThesisEventType.Closed),
            Event(ThesisSubjectType.Candidate, candidate, "MSFT", ThesisEventType.Created),
            Event(ThesisSubjectType.Candidate, candidate, "MSFT", ThesisEventType.Rejected));

        packet.Entries.Select(e => (e.SubjectId, e.EventTypeTerminal))
            .Should().Equal((thesis, ThesisEventType.Closed));
        packet.CounterfactualEntries.Select(e => (e.SubjectId, e.EventTypeTerminal))
            .Should().Equal((candidate, ThesisEventType.Rejected));
    }
}
