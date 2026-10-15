namespace FinanceSentry.Core.Connections;

/// <summary>
/// Turns the wait a provider asked for (a <c>Retry-After</c>) into the <see cref="ProviderFailure.NotBefore"/> a
/// rate-limited failure carries. A wait is capped at <see cref="MaxWait"/>, so one odd header cannot park a
/// connection for good; a missing or non-positive wait names no time.
/// </summary>
public static class RetryAfterHint
{
    public static readonly TimeSpan MaxWait = TimeSpan.FromDays(1);

    public static DateTimeOffset? NotBefore(DateTimeOffset at, TimeSpan? retryAfter) =>
        retryAfter is { } wait && wait > TimeSpan.Zero ? at + (wait > MaxWait ? MaxWait : wait) : null;
}
