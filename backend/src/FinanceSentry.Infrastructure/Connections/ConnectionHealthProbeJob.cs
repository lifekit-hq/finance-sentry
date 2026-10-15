namespace FinanceSentry.Infrastructure.Connections;

using FinanceSentry.Core.Connections;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// The connection-health probe job (report §10 Option B, slice S4): every <see cref="ConnectionHealthOptions.ProbeInterval"/>
/// it asks each sync module (<see cref="IConnectionHealthProbe"/>) for its suspect and Dormant connections and probes
/// the ones due (<see cref="ConnectionHealthPolicy.ProbeDue"/>) with the provider's cheap authenticated call: a suspect
/// credential failure is confirmed (ActionRequired) or cleared, a Dormant connection heals on a success. At most one
/// probe per connection per window, none for a connection that is neither suspect nor Dormant, and none for a provider
/// outside <see cref="ConnectionHealthOptions.ActiveProviders"/>: a shadow provider's due probe is only logged.
/// </summary>
public sealed class ConnectionHealthProbeJob(
    IEnumerable<IConnectionHealthProbe> probes,
    IOptionsMonitor<ConnectionHealthOptions> options,
    TimeProvider time,
    ILogger<ConnectionHealthProbeJob> logger)
{
    /// <summary>The recurring job's id.</summary>
    public const string JobId = "connection-health-probe";

    /// <summary>
    /// Every 30 minutes, off the quarter-hours the sync jobs run on, so a probe does not land on the same provider in
    /// the same minute as a scheduled sync (Monobank answers one call a minute per token).
    /// </summary>
    public const string Cron = "7,37 * * * *";

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var current = options.CurrentValue;
        foreach (var probe in probes)
        {
            IReadOnlyList<ConnectionHealthProbeTarget> candidates;
            try
            {
                candidates = await probe.ListCandidatesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Connection health probe: could not list the candidates of {Probe}", probe.GetType().Name);
                continue;
            }

            foreach (var target in candidates)
                await ProbeIfDueAsync(probe, target, current, ct);
        }
    }

    private async Task ProbeIfDueAsync(
        IConnectionHealthProbe probe, ConnectionHealthProbeTarget target, ConnectionHealthOptions current, CancellationToken ct)
    {
        var subject = target.Subject;
        if (ConnectionHealthPolicy.ProbeDue(target.Health, time.GetUtcNow(), current) is not { } reason
            || (reason == ConnectionHealthProbeReason.Dormant && !target.ProbesDormant))
            return;

        if (!current.IsActive(subject.Provider))
        {
            logger.LogInformation(
                "Connection health (shadow): {Provider} {SubjectKind} {SubjectId} of user {UserId} is due a {ProbeReason} "
                + "probe; as an active provider it would be probed now, and it is left alone",
                subject.Provider,
                subject.Kind,
                subject.Id,
                subject.UserId,
                reason);
            return;
        }

        try
        {
            await probe.ProbeAsync(target, reason, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Connection health probe: the {ProbeReason} probe of {Provider} {SubjectKind} {SubjectId} could not run",
                reason,
                subject.Provider,
                subject.Kind,
                subject.Id);
        }
    }
}
