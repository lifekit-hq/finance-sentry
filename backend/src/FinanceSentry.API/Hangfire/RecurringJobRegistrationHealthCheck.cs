namespace FinanceSentry.API.Hangfire;

using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Readiness check named <c>job-registration</c>. Healthy while <see cref="RecurringJobRegistrationService"/>
/// is still inside its retry budget (a pending registration is expected during a stale-lock window, not
/// a fault) and once it completes; Unhealthy only after the budget is spent, so a missing schedule is a
/// signal at the readiness endpoint rather than a single log line.
/// </summary>
public sealed class RecurringJobRegistrationHealthCheck(RecurringJobRegistrationStatus status) : IHealthCheck
{
    public const string Name = "job-registration";

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(status.State switch
        {
            RecurringJobRegistrationState.Failed => HealthCheckResult.Unhealthy(
                "Recurring job registration gave up after repeated Hangfire lock timeouts; this build's " +
                "job schedule is not registered (jobs from earlier starts still run). Restart the API " +
                "once the lock holder is gone."),
            RecurringJobRegistrationState.Pending => HealthCheckResult.Healthy(
                "Recurring job registration is in progress (retrying while a Hangfire lock is held)."),
            RecurringJobRegistrationState.Skipped => HealthCheckResult.Healthy(
                "Recurring job registration was skipped at startup; see the migrations check."),
            _ => HealthCheckResult.Healthy("Recurring jobs are registered."),
        });
}
