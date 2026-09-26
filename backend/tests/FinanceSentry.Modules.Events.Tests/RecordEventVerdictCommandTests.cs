namespace FinanceSentry.Modules.Events.Tests;

using FinanceSentry.Modules.Events.Application.Commands;
using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Events.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>Feature 049 US3 / feature 687: a verdict is written for any of the user's own, existing
/// events - alert-sourced or not.</summary>
public sealed class RecordEventVerdictCommandTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid AlertId = Guid.NewGuid();

    private readonly Mock<IEventDeliveryReader> _delivery = new();
    private readonly Mock<IEventVerdictRepository> _verdicts = new();

    private RecordEventVerdictCommandHandler Handler() => new(_delivery.Object, _verdicts.Object);

    private void EventExists() => _delivery
        .Setup(d => d.FindAsync(UserId, EventId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new EventDeliveryRecord(EventId, AlertId, "NewsCluster", "Subject", "Delivered", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow));

    [Fact]
    public async Task Writes_the_trimmed_verdict_for_an_owned_event()
    {
        EventExists();
        EventVerdict? saved = null;
        _verdicts.Setup(v => v.UpsertAsync(It.IsAny<EventVerdict>(), It.IsAny<CancellationToken>()))
            .Callback<EventVerdict, CancellationToken>((v, _) => saved = v);

        var ok = await Handler().Handle(new RecordEventVerdictCommand(UserId, EventId, "  Guidance cut is priced in. ", true), CancellationToken.None);

        ok.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.UserId.Should().Be(UserId);
        saved.CompanionEventId.Should().Be(EventId);
        saved.AlertId.Should().Be(AlertId);
        saved.Verdict.Should().Be("Guidance cut is priced in.");
        saved.Notified.Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_or_foreign_event_writes_nothing()
    {
        _delivery.Setup(d => d.FindAsync(UserId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync((EventDeliveryRecord?)null);

        var ok = await Handler().Handle(new RecordEventVerdictCommand(UserId, EventId, "text", false), CancellationToken.None);

        ok.Should().BeFalse();
        _verdicts.Verify(v => v.UpsertAsync(It.IsAny<EventVerdict>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Event_not_captured_from_an_alert_still_writes_a_verdict_with_no_alert_id()
    {
        _delivery.Setup(d => d.FindAsync(UserId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventDeliveryRecord(EventId, null, "AnalystAction", "Subject", "Delivered", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow));
        EventVerdict? saved = null;
        _verdicts.Setup(v => v.UpsertAsync(It.IsAny<EventVerdict>(), It.IsAny<CancellationToken>()))
            .Callback<EventVerdict, CancellationToken>((v, _) => saved = v);

        var ok = await Handler().Handle(new RecordEventVerdictCommand(UserId, EventId, "text", true), CancellationToken.None);

        ok.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.AlertId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_verdict_writes_nothing(string verdict)
    {
        EventExists();

        var ok = await Handler().Handle(new RecordEventVerdictCommand(UserId, EventId, verdict, false), CancellationToken.None);

        ok.Should().BeFalse();
        _verdicts.Verify(v => v.UpsertAsync(It.IsAny<EventVerdict>(), It.IsAny<CancellationToken>()), Times.Never);
        _delivery.Verify(d => d.FindAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Over_long_verdict_is_refused_not_truncated()
    {
        EventExists();

        var ok = await Handler().Handle(
            new RecordEventVerdictCommand(UserId, EventId, new string('x', EventVerdict.MaxVerdictLength + 1), false), CancellationToken.None);

        ok.Should().BeFalse();
        _verdicts.Verify(v => v.UpsertAsync(It.IsAny<EventVerdict>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
