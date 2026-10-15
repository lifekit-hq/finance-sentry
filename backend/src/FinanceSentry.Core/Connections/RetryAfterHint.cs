namespace FinanceSentry.Core.Connections;

/// <summary>
/// Turns the wait a provider asked for (a <c>Retry-After</c>) into the <see cref="ProviderFailure.NotBefore"/> a
/// rate-limited failure carries, as given. A missing or non-positive wait names no time.
/// </summary>
public static class RetryAfterHint
{
    public static DateTimeOffset? NotBefore(DateTimeOffset at, TimeSpan? retryAfter) =>
        retryAfter is { } wait && wait > TimeSpan.Zero ? at + wait : null;
}
