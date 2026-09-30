namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily Hangfire job (#698): reports a look-ahead detector that has gone <see cref="SilenceThreshold"/>
/// without a single alert while its inputs were non-empty. Both detectors ran green for weeks without
/// ever firing, and a green run that never alerts looks exactly like a quiet market — a scheduled job's
/// failure alert cannot catch it. An equity universe produces an earnings or ex-dividend date and a
/// periodic filing at least once per quarterly reporting cycle, so a full cycle of silence means the
/// detector, its provider feed or its filters stopped matching. The report reuses the operational
/// alert path (<see cref="IAlertGeneratorService.GenerateDetectorSilentAlertAsync"/>), is resolved once
/// the detector fires again or its inputs empty out, and is only raised for a detector that has been
/// scheduled for the whole threshold.
/// </summary>
public sealed class LookaheadSilenceMonitorJob(
    IBankingTotalsReader banking,
    LookaheadUniverse universe,
    IAlertFireHistoryReader fireHistory,
    IRecurringJobAgeReader schedules,
    IAlertGeneratorService alerts,
    ILogger<LookaheadSilenceMonitorJob> logger)
{
    /// <summary>
    /// One quarterly reporting cycle less a margin: long enough that the gap between two reporting
    /// seasons never trips it for a multi-name universe, short enough to stay inside the 90-day window
    /// in which resolved and dismissed alerts are still on record.
    /// </summary>
    public static readonly TimeSpan SilenceThreshold = TimeSpan.FromDays(85);

    // The Alerts module's alert-type values for the two detectors (AlertType.EarningsAhead / FilingLanded).
    private const string EarningsAheadAlertType = "EarningsAhead";
    private const string FilingLandedAlertType = "FilingLanded";

    public Task ExecuteAsync(CancellationToken ct = default) => ExecuteAsync(DateTimeOffset.UtcNow, ct);

    /// <summary>Overload taking the reference instant explicitly, so tests aren't at the mercy of the day they run on.</summary>
    public async Task ExecuteAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        Detector[] detectors =
        [
            new(EarningsAheadJob.RecurringJobId, EarningsAheadAlertType, universe.EarningsTickersAsync,
                schedules.GetCreatedAt(EarningsAheadJob.RecurringJobId)),
            new(FilingWatchJob.RecurringJobId, FilingLandedAlertType, universe.FilingTickersAsync,
                schedules.GetCreatedAt(FilingWatchJob.RecurringJobId)),
        ];

        var userIds = await banking.GetActiveUserIdsAsync(ct);

        foreach (var userId in userIds)
        {
            foreach (var detector in detectors)
            {
                try
                {
                    await CheckAsync(userId, detector, now, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "LookaheadSilenceMonitor: error checking {Detector} for user {UserId}",
                        detector.JobId, userId);
                }
            }
        }
    }

    private async Task CheckAsync(Guid userId, Detector detector, DateTimeOffset now, CancellationToken ct)
    {
        var inputs = await detector.Inputs(userId, ct);
        if (inputs.Count == 0)
        {
            await alerts.ResolveDetectorSilentAlertAsync(userId, detector.JobId, ct);
            return;
        }

        var lastRaised = await fireHistory.GetLastRaisedAtAsync(userId, detector.AlertType, ct);
        if (lastRaised is { } last && now - last < SilenceThreshold)
        {
            await alerts.ResolveDetectorSilentAlertAsync(userId, detector.JobId, ct);
            return;
        }

        if (detector.ScheduledAt is not { } scheduled || now - scheduled < SilenceThreshold)
        {
            return;
        }

        var silentFor = now - (lastRaised ?? now - SilenceThreshold);
        logger.LogWarning(
            "LookaheadSilenceMonitor: {Detector} raised nothing for {Days} days over {Inputs} inputs for user {UserId}",
            detector.JobId, (int)silentFor.TotalDays, inputs.Count, userId);

        await alerts.GenerateDetectorSilentAlertAsync(
            userId, detector.JobId, (int)silentFor.TotalDays, inputs.Count, ct);
    }

    private sealed record Detector(
        string JobId,
        string AlertType,
        Func<Guid, CancellationToken, Task<IReadOnlySet<string>>> Inputs,
        DateTimeOffset? ScheduledAt);
}
