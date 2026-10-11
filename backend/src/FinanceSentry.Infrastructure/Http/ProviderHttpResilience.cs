namespace FinanceSentry.Infrastructure.Http;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

/// <summary>
/// The resilience pipeline every outbound provider <see cref="HttpClient"/> shares: the platform's standard
/// handler (<c>Microsoft.Extensions.Http.Resilience</c>) with limits that are no looser than the bare client
/// had. The registration's own <c>client.Timeout</c> stays the outer cap and is passed here as
/// <c>timeout</c>, so the per-attempt budget and the worst-case call duration are what they were before; a
/// retry only happens when an attempt fails fast (5xx, 408, 429, connection error) with budget to spare - a
/// <c>Retry-After</c> longer than half the timeout is not waited for, so the 429 is returned to the caller as
/// it was before instead of the call being cancelled by <see cref="HttpClient.Timeout"/> mid-wait.
/// </summary>
public static class ProviderHttpResilience
{
    /// <summary>Retries after the first attempt (3 attempts in all) for clients that did not retry before.</summary>
    public const int DefaultMaxRetries = 2;

    /// <summary><see cref="HttpClient"/>'s own default timeout, for clients registered without one.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(100);

    // The HttpClient's own timeout stays the real cap, so a call that runs out of time surfaces the same
    // TaskCanceledException as before; the pipeline's total timeout only has to not fire first.
    private static readonly TimeSpan TotalTimeoutMargin = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Adds the standard resilience handler. Retries are limited to safe methods (a POST that did not retry
    /// before does not start retrying) and honour <c>Retry-After</c>. <paramref name="maxRetries"/> of 0 keeps
    /// the timeouts, circuit breaker and telemetry but never retries - for requests that are signed per attempt
    /// (a replayed nonce or timestamp is rejected) or already paced by their caller.
    /// </summary>
    /// <param name="builder">The client registration.</param>
    /// <param name="timeout">The client's configured <see cref="HttpClient.Timeout"/>.</param>
    /// <param name="maxRetries">Retries after the first attempt; 0 disables retrying.</param>
    /// <param name="retryDelay">Base delay of the exponential backoff; the platform default (2 s) when null.</param>
    public static IHttpClientBuilder AddProviderResilience(
        this IHttpClientBuilder builder,
        TimeSpan timeout,
        int maxRetries = DefaultMaxRetries,
        TimeSpan? retryDelay = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);

        builder.AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = timeout;
            options.TotalRequestTimeout.Timeout = timeout + TotalTimeoutMargin;
            // The breaker rejects a sampling window shorter than twice the attempt timeout.
            options.CircuitBreaker.SamplingDuration = Max(options.CircuitBreaker.SamplingDuration, timeout * 2);

            options.Retry.ShouldRetryAfterHeader = true;
            if (maxRetries == 0)
            {
                options.Retry.ShouldHandle = _ => PredicateResult.False();
                return;
            }

            var longestRetryAfter = timeout / 2;
            options.Retry.ShouldHandle = args => ValueTask.FromResult(
                HttpClientResiliencePredicates.IsTransient(args.Outcome)
                && !AsksForLongerThan(args.Outcome.Result, longestRetryAfter));
            options.Retry.DisableForUnsafeHttpMethods();
            options.Retry.MaxRetryAttempts = maxRetries;
            if (retryDelay is { } delay)
                options.Retry.Delay = delay;
        });

        return builder;
    }

    private static bool AsksForLongerThan(HttpResponseMessage? response, TimeSpan limit)
    {
        var retryAfter = response?.Headers.RetryAfter;
        var requested = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        return requested > limit;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;
}
