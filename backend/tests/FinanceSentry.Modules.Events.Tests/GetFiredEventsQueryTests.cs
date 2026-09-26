namespace FinanceSentry.Modules.Events.Tests;

using FinanceSentry.Modules.Events.Application.Queries;
using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Events.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>Feature 049 US3: the feed enriches alerts with delivery and verdicts, and derives the outcome.</summary>
public sealed class GetFiredEventsQueryTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IFiredAlertReader> _alerts = new();
    private readonly Mock<IEventDeliveryReader> _delivery = new();
    private readonly Mock<IEventVerdictRepository> _verdicts = new();

    public GetFiredEventsQueryTests()
    {
        _delivery.Setup(d => d.ListForAlertsAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _verdicts.Setup(v => v.ListByAlertIdsAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private GetFiredEventsQueryHandler Handler() => new(_alerts.Object, _delivery.Object, _verdicts.Object);

    private static FiredAlertRecord Alert(Guid id, string type, string? label = "MU") => new(
        id, type, "Warning", $"{type} title", "message", Guid.NewGuid(), label, false, false, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Requests_the_five_event_types_and_pages_like_alerts()
    {
        IReadOnlyCollection<string>? types = null;
        _alerts.Setup(a => a.ListAsync(UserId, It.IsAny<IReadOnlyCollection<string>>(), 1, 20, It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyCollection<string>, int, int, CancellationToken>((_, t, _, _, _) => types = t)
            .ReturnsAsync(new FiredAlertPage([], 0));

        var result = await Handler().Handle(new GetFiredEventsQuery(UserId, 0, 500), CancellationToken.None);

        types.Should().BeEquivalentTo(FiredEventTypes.All);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalPages.Should().Be(0);
        _delivery.Verify(d => d.ListForAlertsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Each_row_carries_delivery_verdict_and_outcome()
    {
        var silentId = Guid.NewGuid();
        var verdictId = Guid.NewGuid();
        var awaitingId = Guid.NewGuid();
        var suppressedId = Guid.NewGuid();
        var silentEvent = Guid.NewGuid();
        var verdictEvent = Guid.NewGuid();
        var suppressedEvent = Guid.NewGuid();

        _alerts.Setup(a => a.ListAsync(UserId, It.IsAny<IReadOnlyCollection<string>>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FiredAlertPage([
                Alert(silentId, FiredEventTypes.NewsCluster),
                Alert(verdictId, FiredEventTypes.EarningsAhead),
                Alert(awaitingId, FiredEventTypes.BudgetBreach, "Groceries"),
                Alert(suppressedId, FiredEventTypes.MarketStructure),
            ], 4));
        _delivery.Setup(d => d.ListForAlertsAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new EventDeliveryRecord(silentEvent, silentId, "NewsCluster", "Subject", "Delivered", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                new EventDeliveryRecord(verdictEvent, verdictId, "EarningsAhead", "Subject", "Delivered", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow),
                new EventDeliveryRecord(suppressedEvent, suppressedId, "MarketStructure", "Subject", "SuppressedByRateLimit", DateTimeOffset.UtcNow, null, null),
            ]);
        _verdicts.Setup(v => v.ListByAlertIdsAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EventVerdict { UserId = UserId, CompanionEventId = verdictEvent, AlertId = verdictId, Verdict = "Priced in.", Notified = false }]);

        var result = await Handler().Handle(new GetFiredEventsQuery(UserId, 1, 20), CancellationToken.None);

        result.TotalCount.Should().Be(4);
        result.TotalPages.Should().Be(1);
        var byId = result.Items.ToDictionary(i => i.AlertId);
        byId[silentId].Outcome.Should().Be(EventOutcome.Silent);
        byId[silentId].Delivery!.DeliveredAt.Should().NotBeNull();
        byId[silentId].Verdict.Should().BeNull();
        byId[verdictId].Outcome.Should().Be(EventOutcome.JudgedImmaterial);
        byId[verdictId].Verdict!.Text.Should().Be("Priced in.");
        byId[awaitingId].Outcome.Should().Be(EventOutcome.Awaiting);
        byId[awaitingId].Delivery.Should().BeNull();
        byId[awaitingId].Subject.Should().Be("Groceries");
        byId[suppressedId].Outcome.Should().Be(EventOutcome.NotDelivered);
        byId[suppressedId].Delivery!.Disposition.Should().Be("SuppressedByRateLimit");
    }

    [Fact]
    public async Task With_no_outbox_rows_at_all_every_row_is_awaiting_and_nothing_fails()
    {
        _alerts.Setup(a => a.ListAsync(UserId, It.IsAny<IReadOnlyCollection<string>>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FiredAlertPage([Alert(Guid.NewGuid(), FiredEventTypes.FilingLanded)], 1));

        var result = await Handler().Handle(new GetFiredEventsQuery(UserId, 1, 20), CancellationToken.None);

        result.Items.Should().ContainSingle().Which.Outcome.Should().Be(EventOutcome.Awaiting);
    }

    [Fact]
    public async Task A_verdict_still_renders_after_its_companion_row_has_purged()
    {
        var alertId = Guid.NewGuid();
        _alerts.Setup(a => a.ListAsync(UserId, It.IsAny<IReadOnlyCollection<string>>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FiredAlertPage([Alert(alertId, FiredEventTypes.EarningsAhead)], 1));
        _verdicts.Setup(v => v.ListByAlertIdsAsync(UserId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(alertId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EventVerdict { UserId = UserId, CompanionEventId = Guid.NewGuid(), AlertId = alertId, Verdict = "Beat and raised.", Notified = true }]);

        var result = await Handler().Handle(new GetFiredEventsQuery(UserId, 1, 20), CancellationToken.None);

        var row = result.Items.Should().ContainSingle().Subject;
        row.Delivery.Should().BeNull();
        row.Outcome.Should().Be(EventOutcome.Verdict);
        row.Verdict!.Text.Should().Be("Beat and raised.");
    }

    [Fact]
    public async Task Subject_falls_back_to_the_title_when_the_alert_has_no_reference_label()
    {
        _alerts.Setup(a => a.ListAsync(UserId, It.IsAny<IReadOnlyCollection<string>>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FiredAlertPage([Alert(Guid.NewGuid(), FiredEventTypes.NewsCluster, null)], 1));

        var result = await Handler().Handle(new GetFiredEventsQuery(UserId, 1, 20), CancellationToken.None);

        result.Items.Single().Subject.Should().Be("NewsCluster title");
    }
}
