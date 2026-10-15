namespace FinanceSentry.Infrastructure.Http;

using System.Net.Http.Headers;

/// <summary>Reads the wait a provider's <c>Retry-After</c> header asks for, whichever form it uses.</summary>
public static class RetryAfterHeader
{
    /// <summary>The wait asked for, or null when the response names none.</summary>
    public static TimeSpan? WaitFrom(HttpResponseMessage response, DateTimeOffset now) =>
        WaitFrom(response.Headers.RetryAfter, now);

    public static TimeSpan? WaitFrom(RetryConditionHeaderValue? retryAfter, DateTimeOffset now)
    {
        var wait = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - now : null);
        return wait is { } w && w > TimeSpan.Zero ? w : null;
    }
}
