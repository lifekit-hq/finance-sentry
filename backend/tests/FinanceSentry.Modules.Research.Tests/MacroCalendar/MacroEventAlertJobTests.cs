namespace FinanceSentry.Modules.Research.Tests.MacroCalendar;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// #1091: a released macro event is never announced as upcoming, a scheduled one is announced ahead only inside the
/// lookahead, and the release itself gets one reaction while it is still news. Dedup per release key lives in the
/// alert generator (<c>OncePerReference</c>); this pins what the job offers it.
/// </summary>
public sealed class MacroEventAlertJobTests
{
    private const string CpiKey = "macro:CPI:2026-10";

    // CPI (Sep) as BLS released it: 14 October 2026, 08:30 EDT = 12:30Z.
    private static readonly DateTimeOffset CpiAt = new(2026, 10, 14, 12, 30, 0, TimeSpan.Zero);

    private readonly Mock<IBankingTotalsReader> _banking = new();
    private readonly Mock<IMacroCalendarService> _calendar = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly MacroEventAlertJob _job;

    public MacroEventAlertJobTests()
    {
        _job = new MacroEventAlertJob(
            _banking.Object, _calendar.Object, _alerts.Object, TimeProvider.System,
            NullLogger<MacroEventAlertJob>.Instance);
        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId]);
        _calendar.Setup(c => c.SyncAsync(It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(new MacroCalendarSyncResult(0, 0, 0, []));
        Calendar(Cpi(14));
    }

    private static MacroEvent Cpi(int day) => new()
    {
        EventDate = new DateOnly(2026, 10, day),
        EventTime = new TimeOnly(8, 30),
        Event = "CPI (Sep)",
        Region = "US",
        Importance = MacroImportance.High,
        Source = "https://www.bls.gov/schedule/news_release/cpi.htm",
    };

    private void Calendar(params MacroEvent[] rows)
        => _calendar.Setup(c => c.QueryAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, MacroImportance.High, default))
            .ReturnsAsync(rows);

    private void VerifyNoAhead()
        => _alerts.Verify(a => a.GenerateMacroEventAheadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(),
            default), Times.Never);

    private void VerifyNoReleased()
        => _alerts.Verify(a => a.GenerateMacroEventReleasedAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(),
            default), Times.Never);

    [Fact]
    public async Task Released_event_is_never_announced_ahead()
    {
        // The #1091 evening: the print was out at 12:30Z, the agent said "tomorrow" at 20:00Z.
        await _job.ExecuteAsync(new DateTimeOffset(2026, 10, 14, 20, 0, 0, TimeSpan.Zero));

        VerifyNoAhead();
        _alerts.Verify(a => a.ResolveMacroEventAheadAlertAsync(_userId, CpiKey, default), Times.Once);
        _alerts.Verify(a => a.GenerateMacroEventReleasedAlertAsync(
            _userId, CpiKey, "CPI (Sep)", "US", CpiAt, default), Times.Once);
    }

    [Fact]
    public async Task Scheduled_event_inside_the_lookahead_is_announced_ahead()
    {
        await _job.ExecuteAsync(CpiAt - TimeSpan.FromHours(20));

        _alerts.Verify(a => a.GenerateMacroEventAheadAlertAsync(
            _userId, CpiKey, "CPI (Sep)", "US", CpiAt, default), Times.Once);
        VerifyNoReleased();
        _alerts.Verify(a => a.ResolveMacroEventAheadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Scheduled_event_beyond_the_lookahead_is_not_announced_yet()
    {
        await _job.ExecuteAsync(CpiAt - MacroEventAlertJob.Lookahead - TimeSpan.FromMinutes(1));

        VerifyNoAhead();
        VerifyNoReleased();
    }

    [Fact]
    public async Task Release_found_after_the_reaction_window_only_resolves_the_ahead_alert()
    {
        await _job.ExecuteAsync(CpiAt + MacroEventAlertJob.ReactionWindow + TimeSpan.FromMinutes(1));

        VerifyNoAhead();
        VerifyNoReleased();
        _alerts.Verify(a => a.ResolveMacroEventAheadAlertAsync(_userId, CpiKey, default), Times.Once);
    }

    [Fact]
    public async Task Calendar_is_reconciled_before_it_is_read()
    {
        var order = new List<string>();
        _calendar.Setup(c => c.SyncAsync(It.IsAny<DateTimeOffset>(), default))
            .Callback(() => order.Add("sync"))
            .ReturnsAsync(new MacroCalendarSyncResult(0, 1, 0, []));
        _calendar.Setup(c => c.QueryAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, MacroImportance.High, default))
            .Callback(() => order.Add("query"))
            .ReturnsAsync([]);

        await _job.ExecuteAsync(CpiAt);

        Assert.Equal(["sync", "query"], order);
    }

    [Fact]
    public async Task One_users_failure_does_not_stop_the_others()
    {
        var other = Guid.NewGuid();
        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId, other]);
        _alerts.Setup(a => a.ResolveMacroEventAheadAlertAsync(_userId, CpiKey, default))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await _job.ExecuteAsync(new DateTimeOffset(2026, 10, 14, 20, 0, 0, TimeSpan.Zero));

        _alerts.Verify(a => a.GenerateMacroEventReleasedAlertAsync(
            other, CpiKey, "CPI (Sep)", "US", CpiAt, default), Times.Once);
    }
}
