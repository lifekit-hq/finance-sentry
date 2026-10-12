namespace FinanceSentry.Modules.Companion.Infrastructure.Jobs;

using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Forwards saved problem reports to the fleet inbox through the relay, oldest first. With no relay key set up it does
/// nothing and says nothing: reports stay pending and go out once the key is there. A failed send is retried on a
/// widening backoff up to <see cref="ProblemReportLimits.MaxForwardAttempts"/> times; the request id is the report's own,
/// so a retry the relay already received is the same note, not a second one. Overlap-protected.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 120)]
public sealed class ProblemReportForwardJob(
    IProblemReportRepository reports,
    IProblemReportRelay relay,
    ILogger<ProblemReportForwardJob> logger)
{
    private const int BatchLimit = 50;

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        if (!relay.IsConfigured)
        {
            return;
        }

        var due = await reports.ListDueUnscopedAsync(DateTimeOffset.UtcNow, BatchLimit, ct);
        foreach (var report in due)
        {
            var result = await relay.SendAsync(ProblemReportLimits.RequestId(report.Id), ProblemReportNote.Build(report), ct);
            switch (result.Status)
            {
                case RelayStatus.NotConfigured:
                    return;
                case RelayStatus.Deferred:
                    logger.LogInformation("Problem report relay is rate limiting; {Detail}", result.Detail);
                    return;
                case RelayStatus.Sent:
                    report.Status = ProblemReportStatus.Forwarded;
                    report.ForwardedAt = DateTimeOffset.UtcNow;
                    report.NextAttemptAt = null;
                    report.LastError = null;
                    break;
                default:
                    RecordFailure(report, result.Detail);
                    break;
            }

            await reports.UpdateUnscopedAsync(report, ct);
        }
    }

    private void RecordFailure(ProblemReport report, string? detail)
    {
        report.Attempts++;
        report.LastError = detail;
        if (report.Attempts >= ProblemReportLimits.MaxForwardAttempts)
        {
            report.Status = ProblemReportStatus.Failed;
            report.NextAttemptAt = null;
            logger.LogError("Problem report {ReportId} failed after {Attempts} attempts: {Detail}", report.Id, report.Attempts, detail);
            return;
        }

        var backoff = ProblemReportLimits.ForwardBackoff;
        report.NextAttemptAt = DateTimeOffset.UtcNow + backoff[Math.Min(report.Attempts, backoff.Count) - 1];
        logger.LogWarning("Problem report {ReportId} not forwarded (attempt {Attempts}): {Detail}", report.Id, report.Attempts, detail);
    }
}
