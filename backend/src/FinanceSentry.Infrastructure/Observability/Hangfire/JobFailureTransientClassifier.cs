namespace FinanceSentry.Infrastructure.Observability.Hangfire;

using System.Net.Http;
using System.Net.Sockets;
using FinanceSentry.Infrastructure.Retry;

/// <summary>
/// Classifies a failed job's exception as transient/self-healing (US4) so throttling and network blips
/// don't count toward a consecutive-failure streak — mirroring the sync layer's transient set
/// (timeout / rate-limit / 5xx / network) so only genuine, sticky failures alert.
/// </summary>
public static class JobFailureTransientClassifier
{
    public static bool IsTransient(Exception? exception)
    {
        if (exception is null)
            return false;

        // A fan-out job reports every failed unit of work in one AggregateException. Walking
        // InnerException would only ever inspect InnerExceptions[0], so a total outage whose first
        // failure happened to be a timeout was written off as self-healing and never reached the
        // consecutive-failure streak. The run only self-heals if every failure in it does.
        if (exception is AggregateException aggregate)
        {
            var inner = aggregate.Flatten().InnerExceptions;
            return inner.Count > 0 && inner.All(IsTransient);
        }

        if (exception is TimeoutException or TaskCanceledException or OperationCanceledException or SocketException)
            return true;

        if (exception is HttpRequestException http
            && http.StatusCode is { } status
            && RetryPolicies.IsTransientHttpError(status))
            return true;

        // A connect or DNS failure has no status code and, depending on the runtime path, no
        // SocketException inner either; the request error names it.
        if (exception is HttpRequestException { StatusCode: null } net
            && net.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError)
            return true;

        return IsTransient(exception.InnerException);
    }
}
