namespace FinanceSentry.Modules.Events.Tests;

using FinanceSentry.Modules.Events.Application.Queries;
using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Events.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>Feature 687: the per-day accountability view over the companion outbox, not the alert table.</summary>
public sealed class GetDailyEventOutcomesQueryTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 9, 26);

    private readonly Mock<IEventDeliveryReader> _delivery = new();
    private readonly Mock<IEventVerdictRepository> _verdicts = new();

    public GetDailyEventOutcomesQueryTests()
    {
        _verdicts.Setup(v => v.ListByCompanionEventIdsAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private GetDailyEventOutcomesQueryHandler Handler() => new(_delivery.Object, _verdicts.Object);

    [Fact]
    public async Task Empty_day_reports_honest_zeros()
    {
        _delivery.Setup(d => d.ListForDateAsync(UserId, Day, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await Handler().Handle(new GetDailyEventOutcomesQuery(UserId, Day), CancellationToken.None);

        result.Fired.Should().Be(0);
        result.Judged.Should().Be(0);
        result.Sent.Should().Be(0);
        result.Withheld.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Counts_every_kind_including_events_with_no_alert_behind_them()
    {
        var sentId = Guid.NewGuid();
        var withheldId = Guid.NewGuid();
        var silentId = Guid.NewGuid();
        var analystActionId = Guid.NewGuid();

        _delivery.Setup(d => d.ListForDateAsync(UserId, Day, It.IsAny<CancellationToken>())).ReturnsAsync([
            new EventDeliveryRecord(sentId, Guid.NewGuid(), "EarningsAhead", "MU", "Delivered", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new EventDeliveryRecord(withheldId, Guid.NewGuid(), "NewsCluster", "MU", "Delivered", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new EventDeliveryRecord(silentId, Guid.NewGuid(), "MarketStructure", "SPY", "Delivered", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new EventDeliveryRecord(analystActionId, null, "AnalystAction", "Rebalance suggestion", "Delivered", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        ]);
        _verdicts.Setup(v => v.ListByCompanionEventIdsAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new EventVerdict { UserId = UserId, CompanionEventId = sentId, AlertId = Guid.NewGuid(), Verdict = "Material.", Notified = true },
                new EventVerdict { UserId = UserId, CompanionEventId = withheldId, AlertId = Guid.NewGuid(), Verdict = "Priced in.", Notified = false },
                new EventVerdict { UserId = UserId, CompanionEventId = analystActionId, AlertId = null, Verdict = "Executed.", Notified = true },
            ]);

        var result = await Handler().Handle(new GetDailyEventOutcomesQuery(UserId, Day), CancellationToken.None);

        result.Fired.Should().Be(4);
        result.Judged.Should().Be(3);
        result.Sent.Should().Be(2);
        result.Withheld.Should().Be(1);

        var byId = result.Items.ToDictionary(i => i.EventId);
        byId[sentId].Outcome.Should().Be(EventOutcome.Verdict);
        byId[withheldId].Outcome.Should().Be(EventOutcome.JudgedImmaterial);
        byId[silentId].Outcome.Should().Be(EventOutcome.Silent);
        byId[analystActionId].AlertId.Should().BeNull();
        byId[analystActionId].Outcome.Should().Be(EventOutcome.Verdict);
    }
}
