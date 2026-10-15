namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hourly fire-once detector for high-importance macro releases (#1091). Per active user and release it raises at
/// most one <c>MacroEventAhead</c> alert while the release is still scheduled and due within
/// <see cref="Lookahead"/>, and at most one <c>MacroEventReleased</c> alert in the <see cref="ReactionWindow"/>
/// after it is out — both keyed on the release key (<c>macro:CPI:2026-10</c>) and deduped once per reference, ever,
/// so a re-run, a dismiss or the hourly cadence never announces the same release twice, and a released event is
/// never announced as upcoming. The calendar is reconciled with the schedule first, so a misdated row is corrected
/// before anything reads it. The alerts reach the companion like any other, which is how the reader agent learns of
/// a release instead of working it out from dates.
/// </summary>
public sealed class MacroEventAlertJob(
    IBankingTotalsReader banking,
    IMacroCalendarService calendar,
    IAlertGeneratorService alerts,
    TimeProvider clock,
    ILogger<MacroEventAlertJob> logger)
{
    /// <summary>The Hangfire recurring-job id this detector is scheduled under.</summary>
    public const string RecurringJobId = "macro-event-alerts";

    /// <summary>How far ahead a scheduled release is announced.</summary>
    public static readonly TimeSpan Lookahead = TimeSpan.FromHours(48);

    /// <summary>
    /// How long after a release its one reaction may still fire. A release found later than this (the job was down,
    /// or the row was only just corrected into the past) is history: its ahead alert is resolved and nothing is raised.
    /// </summary>
    public static readonly TimeSpan ReactionWindow = TimeSpan.FromHours(12);

    /// <summary>Calendar days read either side of today; covers the lookahead and the publishers' zone offsets.</summary>
    private const int WindowDays = 3;

    public Task ExecuteAsync(CancellationToken ct = default) => ExecuteAsync(clock.GetUtcNow(), ct);

    /// <summary>Overload taking the reference instant explicitly, so tests aren't at the mercy of the hour they run in.</summary>
    public async Task ExecuteAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        await calendar.SyncAsync(now, ct);

        var userIds = await banking.GetActiveUserIdsAsync(ct);
        if (userIds.Count == 0)
        {
            logger.LogDebug("MacroEventAlert: no active users found, skipping.");
            return;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var events = await calendar.QueryAsync(
            today.AddDays(-WindowDays), today.AddDays(WindowDays), null, MacroImportance.High, ct);

        foreach (var userId in userIds)
        {
            try
            {
                foreach (var e in events)
                {
                    await ProcessAsync(userId, e, now, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "MacroEventAlert: error processing user {UserId}", userId);
            }
        }
    }

    private async Task ProcessAsync(Guid userId, MacroEvent e, DateTimeOffset now, CancellationToken ct)
    {
        var at = MacroEventTiming.EventAt(e);
        var key = MacroEventTiming.Key(e);

        if (MacroEventTiming.StatusAt(at, now) == MacroEventStatus.Scheduled)
        {
            if (at - now <= Lookahead)
            {
                await alerts.GenerateMacroEventAheadAlertAsync(userId, key, e.Event, e.Region, at, ct);
            }

            return;
        }

        await alerts.ResolveMacroEventAheadAlertAsync(userId, key, ct);
        if (now - at <= ReactionWindow)
        {
            await alerts.GenerateMacroEventReleasedAlertAsync(userId, key, e.Event, e.Region, at, ct);
        }
    }
}
