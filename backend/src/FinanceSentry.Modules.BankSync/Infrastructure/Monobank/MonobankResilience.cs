namespace FinanceSentry.Modules.BankSync.Infrastructure.Monobank;

using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Retry;

/// <summary>
/// The retry pipeline of the Monobank client. Monobank allows one call per token every ~60 s and answers
/// anything faster with 429, so that is the only status retried (as before: not 5xx, not connection errors,
/// and a webhook POST that was rejected unprocessed is safe to repeat). The wait honours the response's
/// <c>Retry-After</c>, falling back to 60 s then 120 s, and is never longer than 120 s - so the worst case
/// stays at the 3 attempts / 180 s of waiting the hand-written loop had. The pipeline runs inside
/// <c>HttpClient</c>, whose own timeout would count the waits, so the client registers an infinite one and the
/// per-attempt timeout lives here.
/// </summary>
public static class MonobankResilience
{
    public const int MaxRetries = 2;

    /// <summary>The longest a single retry waits, whatever <c>Retry-After</c> asks for.</summary>
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(120);

    /// <summary>Per-attempt timeout - <c>HttpClient</c>'s default, which each attempt had before.</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(100);

    private static readonly TimeSpan[] FallbackDelays = [TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120)];

    public static IHttpClientBuilder AddMonobankResilience(this IHttpClientBuilder builder)
    {
        builder.ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
        builder.AddResilienceHandler("monobank", (pipeline, context) =>
            Configure(pipeline, context.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System));
        return builder;
    }

    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> pipeline, TimeProvider timeProvider)
    {
        pipeline
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = MaxRetries,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests),
                DelayGenerator = args => ValueTask.FromResult<TimeSpan?>(
                    NextDelay(args.AttemptNumber, args.Outcome.Result?.Headers.RetryAfter, timeProvider)),
                // The rejected response is replaced by the next attempt's; nothing else would dispose it.
                OnRetry = args =>
                {
                    args.Outcome.Result?.Dispose();
                    return default;
                }
            })
            .AddTimeout(AttemptTimeout);
    }

    internal static TimeSpan NextDelay(int attemptNumber, RetryConditionHeaderValue? retryAfter, TimeProvider timeProvider)
    {
        var requested = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - timeProvider.GetUtcNow() : null);
        var delay = requested ?? FallbackDelays[Math.Min(attemptNumber, FallbackDelays.Length - 1)];
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }
}
