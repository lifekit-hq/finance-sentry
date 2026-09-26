namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;

/// <summary>
/// Enforces IBKR's Flex Web Service rate limit — 1 request/second and max 10 requests/minute,
/// per token. Registered as a singleton and shared across every Flex call (<c>SendRequest</c>,
/// <c>GetStatement</c>, and each retry poll), because the limit is per-token, not per-endpoint.
/// </summary>
public sealed class IbkrFlexRateLimiter(int maxPerWindow = 10, TimeSpan? window = null, TimeSpan? minInterval = null)
{
    private readonly int _maxPerWindow = maxPerWindow;
    private readonly TimeSpan _window = window ?? TimeSpan.FromMinutes(1);
    private readonly TimeSpan _minInterval = minInterval ?? TimeSpan.FromSeconds(1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<DateTimeOffset> _recent = new();
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            while (true)
            {
                var now = DateTimeOffset.UtcNow;
                while (_recent.Count > 0 && now - _recent.Peek() >= _window)
                    _recent.Dequeue();

                var delay = TimeSpan.Zero;
                var sinceLast = now - _lastRequestAt;
                if (sinceLast < _minInterval)
                    delay = _minInterval - sinceLast;

                if (_recent.Count >= _maxPerWindow)
                {
                    var untilOldestExpires = _window - (now - _recent.Peek());
                    if (untilOldestExpires > delay)
                        delay = untilOldestExpires;
                }

                if (delay <= TimeSpan.Zero)
                {
                    _lastRequestAt = now;
                    _recent.Enqueue(now);
                    return;
                }

                await Task.Delay(delay, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
