using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Events.Application.Commands;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class RecordEventVerdictToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ICommandHandler<RecordEventVerdictCommand, bool>> _handler = new();

    private RecordEventVerdictTool CreateTool(Guid? resolvedUserId = null)
        => new(_handler.Object, new FakeIdentityResolver { ResolvedUserId = resolvedUserId });

    [Fact]
    public async Task ExecuteAsync_ReturnsNotRecorded_WhenIdentityUnresolved()
    {
        var result = await CreateTool().ExecuteAsync(Guid.NewGuid(), "verdict", true);

        result.Recorded.Should().BeFalse();
        _handler.Verify(h => h.Handle(It.IsAny<RecordEventVerdictCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PassesTheIdentityAndArguments_ToTheCommand()
    {
        var eventId = Guid.NewGuid();
        RecordEventVerdictCommand? captured = null;
        _handler.Setup(h => h.Handle(It.IsAny<RecordEventVerdictCommand>(), It.IsAny<CancellationToken>()))
            .Callback<RecordEventVerdictCommand, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync(true);

        var result = await CreateTool(UserId).ExecuteAsync(eventId, "Priced in; no action.", false);

        result.Recorded.Should().BeTrue();
        captured!.UserId.Should().Be(UserId);
        captured.EventId.Should().Be(eventId);
        captured.Verdict.Should().Be("Priced in; no action.");
        captured.Notified.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ExplicitUserId_OverridesTheIdentity()
    {
        var other = Guid.NewGuid();
        RecordEventVerdictCommand? captured = null;
        _handler.Setup(h => h.Handle(It.IsAny<RecordEventVerdictCommand>(), It.IsAny<CancellationToken>()))
            .Callback<RecordEventVerdictCommand, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync(false);

        var result = await CreateTool(UserId).ExecuteAsync(Guid.NewGuid(), "x", true, userId: other);

        result.Recorded.Should().BeFalse();
        captured!.UserId.Should().Be(other);
    }
}
