using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Companion.API.Responses;
using FinanceSentry.Modules.Companion.Application.Commands;
using FinanceSentry.Modules.Companion.Application.Queries;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class LedgerCorrectionToolsTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ICommandHandler<RecordLedgerCorrectionCommand, long?>> _record = new();
    private readonly Mock<IQueryHandler<ListLedgerCorrectionsQuery, LedgerCorrectionsResult>> _list = new();

    private RecordLedgerCorrectionTool RecordTool(Guid? resolvedUserId = null)
        => new(_record.Object, new FakeIdentityResolver { ResolvedUserId = resolvedUserId });

    private ListLedgerCorrectionsTool ListTool(Guid? resolvedUserId = null)
        => new(_list.Object, new FakeIdentityResolver { ResolvedUserId = resolvedUserId });

    [Fact]
    public async Task Record_ReturnsNotRecorded_WhenIdentityUnresolved()
    {
        var result = await RecordTool().ExecuteAsync("said", "wrong", "correction");

        result.Recorded.Should().BeFalse();
        result.Id.Should().BeNull();
        _record.Verify(h => h.Handle(It.IsAny<RecordLedgerCorrectionCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Record_PassesTheIdentityAndArguments_ToTheCommand()
    {
        var at = new DateTimeOffset(2026, 10, 14, 9, 30, 0, TimeSpan.Zero);
        RecordLedgerCorrectionCommand? captured = null;
        _record.Setup(h => h.Handle(It.IsAny<RecordLedgerCorrectionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<RecordLedgerCorrectionCommand, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync(7L);

        var result = await RecordTool(UserId).ExecuteAsync("CPI is on the 12th.", "Wrong date.", "It is the 15th.", at);

        result.Should().Be(new RecordLedgerCorrectionResult(true, 7L));
        captured.Should().Be(new RecordLedgerCorrectionCommand(UserId, "CPI is on the 12th.", "Wrong date.", "It is the 15th.", at));
    }

    [Fact]
    public async Task Record_ReportsNotRecorded_WhenTheCommandRefuses()
    {
        _record.Setup(h => h.Handle(It.IsAny<RecordLedgerCorrectionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);

        var result = await RecordTool(UserId).ExecuteAsync(" ", "wrong", "correction");

        result.Should().Be(new RecordLedgerCorrectionResult(false, null));
    }

    [Fact]
    public async Task List_ReturnsNull_WhenIdentityUnresolved()
    {
        (await ListTool().ExecuteAsync("2026-10")).Should().BeNull();
        _list.Verify(h => h.Handle(It.IsAny<ListLedgerCorrectionsQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("October")]
    [InlineData("2026-13")]
    [InlineData("2026-10-01")]
    public async Task List_ReturnsNull_ForAnUnparseableMonth(string month)
    {
        (await ListTool(UserId).ExecuteAsync(month)).Should().BeNull();
        _list.Verify(h => h.Handle(It.IsAny<ListLedgerCorrectionsQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("2026-09", 0, 2026, 9, 50)]
    [InlineData(" 2026-10 ", 500, 2026, 10, 200)]
    [InlineData("2025-12", 20, 2025, 12, 20)]
    public async Task List_PassesTheMonthAndACappedLimit_ToTheQuery(string month, int limit, int year, int monthNumber, int expectedLimit)
    {
        ListLedgerCorrectionsQuery? captured = null;
        var empty = new LedgerCorrectionsResult(month.Trim(), 0, [], []);
        _list.Setup(h => h.Handle(It.IsAny<ListLedgerCorrectionsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<ListLedgerCorrectionsQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(empty);

        var result = await ListTool(UserId).ExecuteAsync(month, limit);

        result.Should().BeSameAs(empty);
        captured.Should().Be(new ListLedgerCorrectionsQuery(UserId, year, monthNumber, expectedLimit));
    }

    [Fact]
    public async Task List_DefaultsToTheCurrentUtcMonth()
    {
        ListLedgerCorrectionsQuery? captured = null;
        _list.Setup(h => h.Handle(It.IsAny<ListLedgerCorrectionsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<ListLedgerCorrectionsQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(new LedgerCorrectionsResult("", 0, [], []));
        var before = DateTime.UtcNow;

        await ListTool(UserId).ExecuteAsync();

        var after = DateTime.UtcNow;
        captured!.UserId.Should().Be(UserId);
        new[] { (before.Year, before.Month), (after.Year, after.Month) }.Should().Contain((captured.Year, captured.Month));
    }
}
