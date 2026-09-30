namespace FinanceSentry.API.Hangfire;

using global::Hangfire.PostgreSql;
using Microsoft.Extensions.Logging;

/// <summary>
/// The single boundary every startup recurring-job registration passes through. Registering a job
/// takes a storage-level distributed lock (<c>hangfire:lock:recurring-job:&lt;id&gt;</c>) with a fixed
/// 15s timeout; while another process (an overlapping instance during a redeploy, a worker running
/// the job) holds it the registration throws <see cref="PostgreSqlDistributedLockException"/>, which
/// previously escaped Program.cs and killed the host. Registration is idempotent (<c>AddOrUpdate</c>),
/// so the whole pass is safe to repeat.
/// </summary>
public static class StartupJobRegistration
{
    public const int MaxAttempts = 5;

    private static readonly TimeSpan[] DefaultBackoff =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)];

    /// <summary>
    /// Runs <paramref name="register"/>, retrying a lock timeout with bounded backoff. Returns whether
    /// registration completed. When retries are exhausted it logs an error and returns <c>false</c> —
    /// the process stays up, because recurring jobs registered by earlier starts persist in storage
    /// and keep running; only this build's schedule changes are deferred to the next start.
    /// </summary>
    public static bool RegisterWithRetry(
        Action register,
        ILogger logger,
        IReadOnlyList<TimeSpan>? backoff = null)
    {
        var delays = backoff ?? DefaultBackoff;
        var attempts = delays.Count + 1;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                register();
                return true;
            }
            catch (PostgreSqlDistributedLockException ex) when (attempt < attempts)
            {
                var delay = delays[attempt - 1];
                logger.LogWarning(
                    ex,
                    "Startup job registration hit a Hangfire lock timeout (attempt {Attempt}/{Attempts}); retrying in {Delay}.",
                    attempt, attempts, delay);
                Thread.Sleep(delay);
            }
            catch (PostgreSqlDistributedLockException ex)
            {
                logger.LogError(
                    ex,
                    "Startup job registration gave up after {Attempts} attempts on a Hangfire lock timeout. " +
                    "The API stays up with the recurring jobs already in storage; restart it to register this build's jobs.",
                    attempts);
                return false;
            }
        }
    }
}
