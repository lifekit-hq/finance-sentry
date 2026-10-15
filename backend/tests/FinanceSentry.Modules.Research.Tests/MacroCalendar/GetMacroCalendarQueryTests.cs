namespace FinanceSentry.Modules.Research.Tests.MacroCalendar;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// #1091: <c>get_macro_calendar</c> and <c>GET /macro/calendar</c> say whether each event is still to come, so a
/// reader never has to work it out from a date in an unstated zone, and <c>status=scheduled</c> leaves out what is
/// already out.
/// </summary>
public sealed class GetMacroCalendarQueryTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    // The #1091 evening: CPI was released that morning, the FOMC is two weeks out.
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 20, 0, 0, TimeSpan.Zero);

    private readonly GetMacroCalendarQueryHandler _handler;

    public GetMacroCalendarQueryTests()
    {
        var svc = new Mock<IMacroCalendarService>();
        svc.Setup(s => s.QueryAsync(From, To, null, null, default)).ReturnsAsync(
        [
            Row(new DateOnly(2026, 10, 14), new TimeOnly(8, 30), "CPI (Sep)"),
            Row(new DateOnly(2026, 10, 28), new TimeOnly(14, 0), "FOMC rate decision"),
        ]);
        _handler = new GetMacroCalendarQueryHandler(svc.Object, new FixedClock(Now));
    }

    private static MacroEvent Row(DateOnly date, TimeOnly time, string name) => new()
    {
        EventDate = date, EventTime = time, Event = name, Region = "US", Importance = MacroImportance.High,
        Source = "test",
    };

    [Fact]
    public async Task Every_event_carries_its_release_instant_status_and_key()
    {
        var rows = await _handler.Handle(new GetMacroCalendarQuery(From, To, null, null), default);

        rows.Should().HaveCount(2);
        rows[0].EventAt.Should().Be(new DateTimeOffset(2026, 10, 14, 12, 30, 0, TimeSpan.Zero));
        rows[0].Status.Should().Be(MacroEventStatus.Released);
        rows[0].EventKey.Should().Be("macro:CPI:2026-10");
        rows[1].EventAt.Should().Be(new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero));
        rows[1].Status.Should().Be(MacroEventStatus.Scheduled);
        rows[1].EventKey.Should().Be("macro:FOMC:2026-10");
    }

    [Theory]
    [InlineData("scheduled", "FOMC rate decision")]
    [InlineData(" Released ", "CPI (Sep)")]
    public async Task Status_filter_keeps_only_that_status(string status, string kept)
    {
        var rows = await _handler.Handle(new GetMacroCalendarQuery(From, To, null, null, status), default);

        rows.Should().ContainSingle().Which.Event.Should().Be(kept);
    }

    [Fact]
    public async Task Unknown_status_is_rejected_rather_than_narrowed()
    {
        var act = () => _handler.Handle(new GetMacroCalendarQuery(From, To, null, null, "tomorrow"), default);

        (await act.Should().ThrowAsync<MacroStatusInvalidException>())
            .Which.ErrorCode.Should().Be("MACRO_STATUS_INVALID");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
