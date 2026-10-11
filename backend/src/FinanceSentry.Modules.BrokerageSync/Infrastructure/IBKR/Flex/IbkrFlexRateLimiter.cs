namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;

using System.Threading.RateLimiting;

/// <summary>
/// Enforces IBKR's Flex Web Service rate limit — 1 request/second and max 10 requests/minute,
/// per token. Registered as a singleton and shared across every Flex call (<c>SendRequest</c>,
/// <c>GetStatement</c>, and each retry poll), because the limit is per-token, not per-endpoint.
/// Two <see cref="SlidingWindowRateLimiter"/>s queue callers until both allow the request.
/// </summary>
public sealed class IbkrFlexRateLimiter : IDisposable
{
    // One-second segments across a minute. Each window is padded by one segment (see Create), so a permit
    // returns between the window and the window plus one segment after it was taken - never early.
    private const int SegmentsPerWindow = 60;

    private readonly SlidingWindowRateLimiter _perWindow;
    private readonly SlidingWindowRateLimiter? _perInterval;

    public IbkrFlexRateLimiter(int maxPerWindow = 10, TimeSpan? window = null, TimeSpan? minInterval = null)
    {
        _perWindow = Create(maxPerWindow, window ?? TimeSpan.FromMinutes(1), SegmentsPerWindow);

        var interval = minInterval ?? TimeSpan.FromSeconds(1);
        _perInterval = interval > TimeSpan.Zero ? Create(1, interval, 1) : null;
    }

    public async Task WaitAsync(CancellationToken ct = default)
    {
        // The leases are never released: a sliding window hands the permit back when its segment expires.
        await AcquireAsync(_perWindow, ct);
        if (_perInterval is not null)
            await AcquireAsync(_perInterval, ct);
    }

    public void Dispose()
    {
        _perWindow.Dispose();
        _perInterval?.Dispose();
    }

    private static async Task AcquireAsync(RateLimiter limiter, CancellationToken ct)
    {
        using var lease = await limiter.AcquireAsync(1, ct);
        if (!lease.IsAcquired)
            throw new InvalidOperationException("The Flex rate limiter refused a queued request.");
    }

    private static SlidingWindowRateLimiter Create(int permitLimit, TimeSpan window, int segments) => new(
        new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window + window / segments,
            SegmentsPerWindow = segments + 1,
            QueueLimit = int.MaxValue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
}
