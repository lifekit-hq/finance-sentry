namespace FinanceSentry.Infrastructure.Http;

using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;

/// <summary>
/// Classifies a provider's HTTP status as transient (retryable: 5xx, 408, 429) or not, with the platform's
/// own predicate - the one the resilience handlers retry on - so a job's failure classification and the
/// inline retry never disagree. Permanent failures (400, 401, 403, 404, 422, ...) are simply not transient.
/// </summary>
public static class TransientHttpStatus
{
    public static bool IsTransient(HttpStatusCode statusCode)
    {
        using var response = new HttpResponseMessage(statusCode);
        return HttpClientResiliencePredicates.IsTransient(Outcome.FromResult(response));
    }
}
