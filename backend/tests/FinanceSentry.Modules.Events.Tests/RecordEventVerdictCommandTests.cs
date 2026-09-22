namespace FinanceSentry.Modules.Events.Tests;

using FinanceSentry.Modules.Events.Application.Commands;
using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Events.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>Feature 049 US3: a verdict is written only for the user's own, existing event.</summary>
public sealed class RecordEventVerdictCommandTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    private readonly Mock<IEventDeliveryReader> _delivery = new();
    private readonly Mock<IEventVerdictRepository> _verdicts = new();

    private RecordEventVerdictCommandHandler Handler() => new(_delivery.Object, _verdicts.Object);

    private void EventExists() => _delivery
        .Setup(d => d.FindAsync(UserId, EventId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new EventDeliveryRecord(EventId, Guid.NewGuid(), "NewsCluster", "Delivered", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow));

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
